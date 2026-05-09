using CbtExam.Data;
using CbtExam.Shared.DTOs;
using CbtExam.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CbtExam.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SessionsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await db.ExamSessions
            .Include(s => s.Exam)
            .Select(s => new SessionDto(s.Id, s.ExamId, s.Exam!.Title, s.SessionCode, s.StartedAt, s.IsActive, s.StudentExams.Count))
            .ToListAsync());

    [HttpPost("start")]
    public async Task<IActionResult> Start(SessionStartDto dto)
    {
        var exam = await db.Exams.FindAsync(dto.ExamId);
        if (exam is null) return NotFound("Exam not found");

        // Deactivate any existing active session for this exam
        var existing = await db.ExamSessions.Where(s => s.ExamId == dto.ExamId && s.IsActive).ToListAsync();
        existing.ForEach(s => { s.IsActive = false; s.EndedAt = DateTime.UtcNow; });

        var session = new ExamSession
        {
            ExamId = dto.ExamId,
            SessionCode = GenerateCode(),
            StartedAt = DateTime.UtcNow,
            IsActive = true
        };
        db.ExamSessions.Add(session);
        await db.SaveChangesAsync();
        return Ok(new SessionDto(session.Id, session.ExamId, exam.Title, session.SessionCode, session.StartedAt, true, 0));
    }

    [HttpPost("{id}/stop")]
    public async Task<IActionResult> Stop(int id)
    {
        var session = await db.ExamSessions.FindAsync(id);
        if (session is null) return NotFound();
        session.IsActive = false;
        session.EndedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("end-all")]
    public async Task<IActionResult> EndAll()
    {
        var active = await db.ExamSessions.Where(s => s.IsActive).ToListAsync();
        if (active.Count == 0) return Ok(0);
        foreach (var session in active)
        {
            session.IsActive = false;
            session.EndedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        return Ok(active.Count);
    }

    [HttpGet("{id}/students")]
    public async Task<IActionResult> GetStudents(int id) =>
        Ok(await db.StudentExams
            .Include(se => se.Student)
            .Where(se => se.SessionId == id)
            .Select(se => new StudentStatusDto(
                se.Id, se.Student!.FullName, se.Student.StudentId,
                se.JoinedAt, se.IsSubmitted, se.TabSwitchCount,
                se.Answers.Count, se.Answers.Count, 0, !se.IsSubmitted, se.IsSubmitted ? "submitted" : "online"))
            .ToListAsync());

    [HttpGet("{id}/results")]
    public async Task<IActionResult> GetResults(int id) =>
        Ok(await db.StudentExams
            .Include(se => se.Student)
            .Where(se => se.SessionId == id && se.IsSubmitted)
            .Select(se => new ResultDto(
                se.Id, se.Student!.FullName, se.Student.StudentId,
                se.Score, se.Session!.Exam!.Questions.Count,
                se.Session.Exam.Questions.Count == 0 ? 0 : Math.Round((double)se.Score / se.Session.Exam.Questions.Count * 100, 1),
                se.SubmittedAt))
            .ToListAsync());

    private static string GenerateCode() =>
        Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper();
}
