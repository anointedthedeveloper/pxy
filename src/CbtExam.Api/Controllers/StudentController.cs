using CbtExam.Api.Hubs;
using CbtExam.Api.Services;
using CbtExam.Data;
using CbtExam.Shared.DTOs;
using CbtExam.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CbtExam.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StudentController(AppDbContext db, IHubContext<ExamHub> hub, SnapshotExportService exports) : ControllerBase
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> _decryptionKeys = new();

    // studentDbId -> deviceId currently holding the session lock
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> _activeSessions = new();

    public static string GetDecryptionKey(int sessionId)
    {
        return _decryptionKeys.GetOrAdd(sessionId, _ => Guid.NewGuid().ToString("N"));
    }

    private static string EncryptAes(string plainText, string keyString)
    {
        byte[] key = System.Text.Encoding.UTF8.GetBytes(keyString.PadRight(32).Substring(0, 32));
        byte[] iv = System.Text.Encoding.UTF8.GetBytes(keyString.PadRight(16).Substring(0, 16));
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        using var ms = new System.IO.MemoryStream();
        using (var cs = new System.Security.Cryptography.CryptoStream(ms, encryptor, System.Security.Cryptography.CryptoStreamMode.Write))
        {
            using (var sw = new System.IO.StreamWriter(cs))
            {
                sw.Write(plainText);
            }
        }
        return Convert.ToBase64String(ms.ToArray());
    }

    // POST /api/student/login
    [HttpPost("login")]
    public async Task<IActionResult> Login(StudentLoginDto dto)
    {
        var username = (dto.StudentId ?? "").Trim();
        var password = (dto.Password ?? "").Trim();
        var deviceId = (dto.DeviceId ?? "").Trim();

        var activeStudents = await db.Students.Where(s => s.IsActive).ToListAsync();
        var student = activeStudents.FirstOrDefault(s =>
            (s.StudentId.Equals(username, StringComparison.OrdinalIgnoreCase) ||
             s.FullName.Equals(username, StringComparison.OrdinalIgnoreCase)) &&
            s.Password.Equals(password, StringComparison.OrdinalIgnoreCase));

        if (student is null)
            return Unauthorized(new { error = "Invalid student credentials or inactive account." });

        // Enforce single active session — reject if already logged in on a different device
        if (_activeSessions.TryGetValue(student.Id, out var lockedDevice))
        {
            if (!string.IsNullOrWhiteSpace(deviceId) && !lockedDevice.Equals(deviceId, StringComparison.OrdinalIgnoreCase))
                return Conflict(new { error = "This account is already logged in on another device. Please ask the invigilator to reset your session." });
        }

        // Record or refresh this device as the active session holder
        if (!string.IsNullOrWhiteSpace(deviceId))
            _activeSessions[student.Id] = deviceId;

        return Ok(new StudentAdminDto(student.Id, student.FullName, student.StudentId, student.IsActive, student.Password));
    }

    // GET /api/student/active-session/{examId}
    // Public endpoint — students poll this to detect when admin opens a room for their exam
    [HttpGet("active-session/{examId}")]
    public async Task<IActionResult> GetActiveSession(int examId)
    {
        var session = await db.ExamSessions
            .Include(s => s.Exam)
            .Where(s => s.ExamId == examId && s.IsActive)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync();

        if (session is null) return Ok(new { found = false });

        return Ok(new
        {
            found      = true,
            id         = session.Id,
            examId     = session.ExamId,
            examTitle  = session.Exam?.Title ?? "",
            sessionCode = session.SessionCode,
            isActive   = session.IsActive,
            isStarted  = session.IsStarted
        });
    }

    // POST /api/student/join
    [HttpPost("join")]
    public async Task<IActionResult> Join(JoinExamDto dto)
    {
        var session = await db.ExamSessions
            .Include(s => s.Exam)
            .FirstOrDefaultAsync(s => s.SessionCode == dto.SessionCode && s.IsActive);

        if (session is null) return BadRequest("Invalid or inactive session code.");

        // Find or create student
        var student = await db.Students.FirstOrDefaultAsync(s => s.StudentId == dto.StudentId)
            ?? new Student { FullName = dto.FullName, StudentId = dto.StudentId };

        if (student.Id == 0)
        {
            db.Students.Add(student);
            await db.SaveChangesAsync();
        }

        // Check if already joined
        var existing = await db.StudentExams.FirstOrDefaultAsync(se => se.StudentId == student.Id && se.SessionId == session.Id);
        if (existing is not null)
        {
            // If retakes are not allowed and student already submitted, reject
            if (!session.AllowRetakes && existing.IsSubmitted)
            {
                return BadRequest("You have already taken this exam and retakes are not allowed.");
            }
            // If retakes are allowed or not submitted, allow rejoining
            return Ok(new JoinResultDto(existing.Id, session.Id, session.ExamId, session.Exam!.Title, session.Exam.DurationMinutes));
        }

        var se = new StudentExam { StudentId = student.Id, SessionId = session.Id, DeviceId = dto.DeviceId, DeviceName = dto.DeviceName };
        
        // Auto-approve if:
        // 1. Session has auto-approve enabled, OR
        // 2. Exam hasn't started yet (students joining waiting room don't need approval)
        // Only require approval for ongoing exams when auto-approve is toggled off
        if (session.AutoApprove || !session.IsStarted)
        {
            se.IsApproved = true;
        }
        
        db.StudentExams.Add(se);
        await db.SaveChangesAsync();

        // Only notify admin if not auto-approved (for manual approval)
        if (!se.IsApproved)
        {
            await NotifyAdmin(session.SessionCode, session.Id);
        }
        
        return Ok(new JoinResultDto(se.Id, session.Id, session.ExamId, session.Exam!.Title, session.Exam.DurationMinutes));
    }

    // GET /api/student/{studentExamId}/status — check approval status
    [HttpGet("{studentExamId}/status")]
    public async Task<IActionResult> GetStatus(int studentExamId)
    {
        var se = await db.StudentExams.FindAsync(studentExamId);
        if (se is null) return NotFound();
        return Ok(new { isApproved = se.IsApproved });
    }

    // GET /api/student/{studentExamId}/questions
    [HttpGet("{studentExamId}/questions")]
    public async Task<IActionResult> GetQuestions(int studentExamId)
    {
        var se = await db.StudentExams.Include(x => x.Session).ThenInclude(s => s!.Exam).ThenInclude(e => e!.Questions)
            .FirstOrDefaultAsync(x => x.Id == studentExamId);

        if (se is null) return NotFound();
        if (se.IsSubmitted) return BadRequest("Exam already submitted.");

        var exam = se.Session!.Exam!;
        var shuffled = QuestionShuffler.ShuffleAll(exam.Questions, exam.ShuffleQuestions, exam.ShuffleOptions, se.Id);
        
        // Return with exam duration included
        return Ok(new { questions = shuffled, durationMinutes = exam.DurationMinutes, examTitle = exam.Title });
    }

    // GET /api/student/{studentExamId}/questions/secure
    [HttpGet("{studentExamId}/questions/secure")]
    public async Task<IActionResult> GetQuestionsSecure(int studentExamId)
    {
        var se = await db.StudentExams.Include(x => x.Session).ThenInclude(s => s!.Exam).ThenInclude(e => e!.Questions)
            .FirstOrDefaultAsync(x => x.Id == studentExamId);

        if (se is null) return NotFound();
        // If already submitted, return a specific status so the client can redirect gracefully
        if (se.IsSubmitted) return Conflict(new { error = "already_submitted" });

        var exam = se.Session!.Exam!;
        var shuffled = QuestionShuffler.ShuffleAll(exam.Questions, exam.ShuffleQuestions, exam.ShuffleOptions, se.Id);
        
        var json = System.Text.Json.JsonSerializer.Serialize(shuffled);
        var key = GetDecryptionKey(se.SessionId);
        var encrypted = EncryptAes(json, key);
        
        return Ok(new { encryptedQuestions = encrypted, isApproved = se.IsApproved });
    }

    // GET /api/student/{studentId}/completed-exams
    [HttpGet("{studentId}/completed-exams")]
    public async Task<IActionResult> GetCompletedExams(string studentId)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.StudentId == studentId);
        if (student is null) return NotFound();

        var completedExams = await db.StudentExams
            .Include(se => se.Session)
            .Where(se => se.StudentId == student.Id && se.IsSubmitted)
            .Select(se => new { se.SessionId, se.Session.SessionCode })
            .ToListAsync();

        return Ok(completedExams);
    }

    // GET /api/student/{studentExamId}/progress
    [HttpGet("{studentExamId}/progress")]
    public async Task<IActionResult> GetProgress(int studentExamId)
    {
        var se = await db.StudentExams
            .Include(x => x.Answers)
            .FirstOrDefaultAsync(x => x.Id == studentExamId);
        if (se is null) return NotFound();

        var answers = se.Answers
            .Select(a => new AnswerSubmitDto(a.QuestionId, a.SelectedAnswer))
            .ToList();
        return Ok(new StudentProgressDto(se.Id, se.IsSubmitted, answers));
    }

    // POST /api/student/progress
    [HttpPost("progress")]
    public async Task<IActionResult> SaveProgress([FromBody] ProgressSaveDto dto)
    {
        var se = await db.StudentExams
            .Include(x => x.Session)
            .Include(x => x.Answers)
            .FirstOrDefaultAsync(x => x.Id == dto.StudentExamId);
        if (se is null) return NotFound();
        if (se.IsSubmitted) return BadRequest("Exam already submitted.");

        var existing = se.Answers.FirstOrDefault(a => a.QuestionId == dto.QuestionId);
        if (existing is null)
        {
            db.Answers.Add(new Answer
            {
                StudentExamId = se.Id,
                QuestionId = dto.QuestionId,
                SelectedAnswer = dto.SelectedAnswer,
                IsCorrect = false
            });
        }
        else
        {
            existing.SelectedAnswer = dto.SelectedAnswer;
        }

        await db.SaveChangesAsync();
        await exports.ExportAllAsync();
        await NotifyAdmin(se.Session!.SessionCode, se.SessionId);
        return Ok();
    }

    // POST /api/student/{studentExamId}/submit
    [HttpPost("{studentExamId}/submit")]
    public async Task<IActionResult> Submit(int studentExamId, [FromBody] ExamSubmitDto dto)
    {
        if (dto is null)
            return BadRequest(new { error = "Invalid submission payload." });

        try
        {
        var se = await db.StudentExams
            .Include(x => x.Session).ThenInclude(s => s!.Exam).ThenInclude(e => e!.Questions)
            .Include(x => x.Answers)
            .FirstOrDefaultAsync(x => x.Id == studentExamId);

        if (se is null) return NotFound(new { error = "Student exam not found." });
        if (se.IsSubmitted)
        {
            var total2 = se.Session?.Exam?.Questions?.Count ?? 0;
            return Ok(new SubmitResultDto(se.Score, total2, total2 == 0 ? 0 : Math.Round((double)se.Score / total2 * 100, 1)));
        }

        var questions = se.Session!.Exam!.Questions.ToDictionary(q => q.Id);
        int score = 0;

        var subjectGroups = questions.Values
            .GroupBy(q => q.Subject ?? "General", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var correctBySubject = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var sub in subjectGroups.Keys) correctBySubject[sub] = 0;

        var answers = dto.Answers ?? [];
        foreach (var ans in answers.DistinctBy(a => a.QuestionId))
        {
            if (!questions.TryGetValue(ans.QuestionId, out var q)) continue;
            bool correct = q.CorrectAnswer.Equals(ans.SelectedAnswer?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);
            if (correct)
            {
                score++;
                var sub = q.Subject ?? "General";
                correctBySubject[sub] = correctBySubject.GetValueOrDefault(sub, 0) + 1;
            }

            var existing = se.Answers.FirstOrDefault(a => a.QuestionId == ans.QuestionId);
            if (existing is null)
            {
                db.Answers.Add(new Answer
                {
                    StudentExamId = se.Id,
                    QuestionId = ans.QuestionId,
                    SelectedAnswer = ans.SelectedAnswer ?? "",
                    IsCorrect = correct
                });
            }
            else
            {
                existing.SelectedAnswer = ans.SelectedAnswer ?? "";
                existing.IsCorrect = correct;
            }
        }

        se.IsSubmitted = true;
        se.SubmittedAt = DateTime.UtcNow;
        se.Score = score;
        await db.SaveChangesAsync();

        _activeSessions.TryRemove(se.StudentId, out _);

        var total = questions.Count;

        double jambTotal = 0;
        var breakdown = new List<string>();
        foreach (var (sub, qList) in subjectGroups)
        {
            int pool = qList.Count;
            int correct = correctBySubject.GetValueOrDefault(sub, 0);
            double scaled = pool > 0 ? Math.Round((double)correct / pool * 100) : 0;
            jambTotal += scaled;
            var abbreviated = AbbreviateSubject(sub);
            breakdown.Add($"{abbreviated}: {correct}/{pool} ({(int)scaled}/100)");
        }

        double maxScore = subjectGroups.Count * 100.0;
        double percentage = maxScore > 0 ? Math.Round(jambTotal / maxScore * 100) : 0;

        // Fire-and-forget export/notify — do NOT await inside try so cycle errors don't fail the response
        _ = Task.Run(async () => {
            try { await exports.ExportAllAsync(); } catch { }
            try { await NotifyAdmin(se.Session!.SessionCode, se.SessionId); } catch { }
        });

        return Ok(new SubmitResultDto(score, total, percentage) { JambScore = Math.Round(jambTotal), SubjectBreakdown = string.Join(" | ", breakdown) });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Submit failed: " + ex.Message });
        }
    }

    // POST /api/student/tabswitch
    [HttpPost("tabswitch")]
    public async Task<IActionResult> TabSwitch(TabSwitchDto dto)
    {
        var se = await db.StudentExams.Include(x => x.Session).FirstOrDefaultAsync(x => x.Id == dto.StudentExamId);
        if (se is null) return NotFound();
        se.TabSwitchCount++;
        await db.SaveChangesAsync();
        await NotifyAdmin(se.Session!.SessionCode, se.SessionId);
        return Ok();
    }

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat(DeviceHeartbeatDto dto)
    {
        var se = await db.StudentExams
            .Include(x => x.Session)
            .Include(x => x.Student)
            .FirstOrDefaultAsync(x => x.Id == dto.StudentExamId);
        if (se is null) return NotFound();

        // Always upsert device so LastSeen stays fresh regardless of whether /device was called first
        if (!string.IsNullOrWhiteSpace(dto.DeviceId))
        {
            var device = await db.Devices.FirstOrDefaultAsync(d => d.DeviceId == dto.DeviceId);
            var studentIdStr = se.Student?.StudentId ?? "";
            if (device is null)
            {
                device = new Device
                {
                    DeviceId = dto.DeviceId,
                    DeviceName = dto.DeviceName,
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    LastSeen = DateTime.UtcNow,
                    IsOnline = true,
                    BatteryLevel = dto.BatteryLevel,
                    StudentId = studentIdStr
                };
                db.Devices.Add(device);
            }
            else
            {
                device.LastSeen = DateTime.UtcNow;
                device.IsOnline = true;
                device.BatteryLevel = dto.BatteryLevel;
                if (!string.IsNullOrWhiteSpace(dto.DeviceName)) device.DeviceName = dto.DeviceName;
                if (!string.IsNullOrWhiteSpace(studentIdStr)) device.StudentId = studentIdStr;
            }
            await db.SaveChangesAsync();
        }

        await NotifyAdmin(se.Session!.SessionCode, se.SessionId, dto);

        var isStarted = se.Session?.IsStarted ?? false;
        var decKey = isStarted ? GetDecryptionKey(se.SessionId) : "";

        return Ok(new {
            broadcastMessage = SessionsController.GetBroadcast(se.SessionId),
            isStarted = isStarted,
            decryptionKey = decKey
        });
    }

    [HttpPost("device")]
    public async Task<IActionResult> RegisterDevice(DeviceRegistrationDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.DeviceId)) return BadRequest();
        
        var device = await db.Devices.FirstOrDefaultAsync(d => d.DeviceId == dto.DeviceId);
        if (device is null)
        {
            device = new Device
            {
                DeviceId = dto.DeviceId,
                DeviceName = dto.DeviceName,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                LastSeen = DateTime.UtcNow,
                IsOnline = true,
                BatteryLevel = dto.BatteryLevel,
                StudentId = string.IsNullOrWhiteSpace(dto.StudentId) ? "Awaiting Login" : dto.StudentId
            };
            db.Devices.Add(device);
        }
        else
        {
            device.DeviceName = dto.DeviceName;
            device.LastSeen = DateTime.UtcNow;
            device.IsOnline = true;
            device.IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? device.IpAddress;
            device.BatteryLevel = dto.BatteryLevel;
            device.StudentId = string.IsNullOrWhiteSpace(dto.StudentId) ? "Awaiting Login" : dto.StudentId;
        }

        await db.SaveChangesAsync();
        // Notify admin about device count update
        await hub.Clients.All.SendAsync("DeviceUpdate");
        return Ok();
    }

    [HttpGet("devices")]
    public async Task<IActionResult> GetDevices()
    {
        var threshold = DateTime.UtcNow.AddSeconds(-12);
        var devices = await db.Devices.ToListAsync();
        bool changed = false;
        foreach (var d in devices)
        {
            if (d.LastSeen < threshold && d.IsOnline)
            {
                d.IsOnline = false;
                changed = true;
            }
        }
        if (changed)
        {
            await db.SaveChangesAsync();
        }

        var dtos = new List<DeviceDto>();
        foreach (var d in devices)
        {
            string studentName = "Awaiting Login";
            string examTitle = "—";
            string examStatus = "Idle";

            if (!string.IsNullOrWhiteSpace(d.StudentId) && d.StudentId != "Awaiting Login")
            {
                var student = await db.Students
                    .Include(s => s.StudentExams)
                    .ThenInclude(se => se.Session)
                    .ThenInclude(session => session!.Exam)
                    .FirstOrDefaultAsync(s => s.StudentId == d.StudentId);
                
                if (student is not null)
                {
                    studentName = student.FullName;
                    var studentExam = student.StudentExams
                        .OrderByDescending(se => se.JoinedAt)
                        .FirstOrDefault();

                    if (studentExam is not null)
                    {
                        examTitle = studentExam.Session?.Exam?.Title ?? "—";
                        if (studentExam.IsSubmitted)
                        {
                            examStatus = "Submitted";
                        }
                        else if (studentExam.Session?.IsStarted == true)
                        {
                            examStatus = "Examining";
                        }
                        else
                        {
                            examStatus = "Waiting Room";
                        }
                    }
                    else
                    {
                        examStatus = "Selecting Exam";
                    }
                }
            }

            dtos.Add(new DeviceDto(
                d.DeviceId,
                d.DeviceName,
                d.IpAddress,
                d.LastSeen,
                d.IsOnline,
                d.BatteryLevel,
                d.StudentId,
                studentName,
                examTitle,
                examStatus
            ));
        }

        return Ok(dtos);
    }

    // POST /api/student/{studentDbId}/reset-session  (admin only)
    [HttpPost("{studentDbId}/reset-session")]
    public IActionResult ResetSession(int studentDbId)
    {
        var adminKey = Environment.GetEnvironmentVariable("CBT_ADMIN_KEY") ?? "admin123";
        if (!Request.Headers.TryGetValue("X-Admin-Key", out var key) || key != adminKey)
            return Unauthorized();
        _activeSessions.TryRemove(studentDbId, out _);
        return Ok(new { message = "Session lock cleared." });
    }

    [HttpPost("snapshot")]
    public async Task<IActionResult> Snapshot(SnapshotDto dto)
    {
        if (dto.StudentExamId <= 0 || string.IsNullOrWhiteSpace(dto.ImageBase64)) return BadRequest();
        try
        {
            await exports.SaveSnapshotAsync(dto.StudentExamId, dto.ImageBase64);
            return Ok();
        }
        catch
        {
            return BadRequest("Invalid snapshot payload.");
        }
    }

    private async Task NotifyAdmin(string sessionCode, int sessionId, DeviceHeartbeatDto? heartbeat = null)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-30);

        var studentExams = await db.StudentExams
            .Include(se => se.Student)
            .Include(se => se.Answers)
            .Where(se => se.SessionId == sessionId)
            .ToListAsync();

        var studentIds = studentExams
            .Select(se => se.Student?.StudentId)
            .Where(sid => !string.IsNullOrEmpty(sid))
            .Distinct()
            .ToList();

        var devices = await db.Devices
            .Where(d => studentIds.Contains(d.StudentId))
            .ToListAsync();

        var deviceMap = devices
            .GroupBy(d => d.StudentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var students = studentExams.Select(se => {
            var studentId = se.Student?.StudentId ?? "";
            deviceMap.TryGetValue(studentId, out var device);

            bool isOnline = device != null && device.LastSeen > cutoff && device.IsOnline;
            string status = se.IsSubmitted ? "submitted" : (isOnline ? "online" : "disconnected");

            var dto = new StudentStatusDto(
                se.Id, 
                se.Student?.FullName ?? "Unknown", 
                studentId, 
                se.JoinedAt, 
                se.IsSubmitted, 
                se.TabSwitchCount,
                se.Answers.Count, 
                0, 
                device?.BatteryLevel ?? 0, 
                isOnline, 
                status, 
                device?.DeviceName ?? "Unknown", 
                device?.DeviceId ?? ""
            );

            // Override with current heartbeat if applicable
            if (heartbeat is not null && se.Id == heartbeat.StudentExamId)
            {
                dto = dto with
                {
                    CurrentQuestion = heartbeat.CurrentQuestion,
                    BatteryLevel = heartbeat.BatteryLevel,
                    IsOnline = heartbeat.IsOnline,
                    ConnectionState = heartbeat.ConnectionState,
                    DeviceName = heartbeat.DeviceName,
                    DeviceId = heartbeat.DeviceId
                };
            }

            return dto;
        }).ToList();

        await ExamHub.NotifyStudentUpdate(hub, sessionCode, students);
    }

    private static string AbbreviateSubject(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return "GEN";
        
        var s = subject.ToLower().Trim();
        
        // Common abbreviations
        if (s.Contains("english")) return "ENG";
        if (s.Contains("mathematics") || s.Contains("math")) return "MATHS";
        if (s.Contains("physics")) return "PHYS";
        if (s.Contains("chemistry") || s.Contains("chem")) return "CHEM";
        if (s.Contains("biology") || s.Contains("bio")) return "BIO";
        if (s.Contains("islamic") || s.Contains("irk")) return "IRK";
        if (s.Contains("christian") || s.Contains("crk")) return "CRK";
        if (s.Contains("geography") || s.Contains("geog")) return "GEOG";
        if (s.Contains("economics") || s.Contains("econ")) return "ECON";
        if (s.Contains("government") || s.Contains("govt")) return "GOVT";
        if (s.Contains("literature") || s.Contains("lit")) return "LIT";
        if (s.Contains("civic") || s.Contains("civ")) return "CIV";
        if (s.Contains("agriculture") || s.Contains("agric")) return "AGRIC";
        if (s.Contains("commerce") || s.Contains("comm")) return "COMM";
        if (s.Contains("account") || s.Contains("acct")) return "ACCT";
        if (s.Contains("french")) return "FREN";
        if (s.Contains("history") || s.Contains("hist")) return "HIST";
        if (s.Contains("geography")) return "GEOG";
        if (s.Contains("general")) return "GEN";
        
        // Default: take first 4 letters uppercase
        return s.Length >= 4 ? s.Substring(0, 4).ToUpper() : s.ToUpper();
    }
}
