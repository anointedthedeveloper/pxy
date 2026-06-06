using CbtExam.Data;
using CbtExam.Shared.DTOs;
using CbtExam.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CbtExam.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class QuestionBankController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? subject, [FromQuery] int? year)
    {
        var q = db.QuestionBank.AsQueryable();
        if (!string.IsNullOrWhiteSpace(subject)) q = q.Where(x => x.Subject == subject);
        if (year.HasValue) q = q.Where(x => x.Year == year.Value);
        return Ok(await q.OrderBy(x => x.Subject).ThenBy(x => x.Year).ThenBy(x => x.QuestionNumber)
            .Select(x => new QuestionBankDto(x.Id, x.Subject, x.Year, x.QuestionNumber, x.Text, x.OptionsJson, x.CorrectAnswer, x.Section, x.ImageUrl))
            .ToListAsync());
    }

    [HttpGet("subjects")]
    public async Task<IActionResult> GetSubjects() =>
        Ok(await db.QuestionBank.Select(q => q.Subject).Distinct().OrderBy(s => s).ToListAsync());

    [HttpGet("years")]
    public async Task<IActionResult> GetYears([FromQuery] string subject) =>
        Ok(await db.QuestionBank.Where(q => q.Subject == subject).Select(q => q.Year).Distinct().OrderBy(y => y).ToListAsync());

    [HttpPost]
    public async Task<IActionResult> Add(QuestionBankCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Text)) return BadRequest("Question text required.");
        if (dto.Options is null || dto.Options.Count < 2) return BadRequest("At least 2 options required.");

        var resolvedAnswer = ResolveCorrectAnswer(dto.CorrectAnswer, dto.Options);
        if (!dto.Options.Contains(resolvedAnswer, StringComparer.OrdinalIgnoreCase))
            return BadRequest("Correct answer must match one of the options.");

        var q = new QuestionBank
        {
            Subject = dto.Subject.Trim(),
            Year = dto.Year,
            QuestionNumber = dto.QuestionNumber,
            Text = dto.Text.Trim(),
            OptionsJson = JsonSerializer.Serialize(dto.Options),
            CorrectAnswer = resolvedAnswer,
            Section = dto.Section ?? string.Empty,
            ImageUrl = dto.ImageUrl ?? string.Empty
        };
        db.QuestionBank.Add(q);
        await db.SaveChangesAsync();
        return Ok(q.Id);
    }

    // Accepts P4JQ repo format — called once per subject file from the desktop
    [HttpPost("import-repo")]
    public async Task<IActionResult> ImportRepo([FromQuery] string subject, [FromBody] List<RepoQuestionDto> questions)
    {
        if (questions == null || questions.Count == 0) return BadRequest("No questions provided.");

        var subjectClean = System.Globalization.CultureInfo.CurrentCulture.TextInfo
            .ToTitleCase(subject.Trim().ToLower());

        static string CapFirst(string s) =>
            string.IsNullOrWhiteSpace(s) ? s : char.ToUpper(s[0]) + s[1..];

        // Pre-load existing (subject, year, qNum) keys to avoid per-row DB round-trips
        var existingKeys = await db.QuestionBank
            .Where(x => x.Subject == subjectClean)
            .Select(x => new { x.Year, x.QuestionNumber })
            .ToListAsync();
        var existingSet = existingKeys.Select(x => (x.Year, x.QuestionNumber)).ToHashSet();

        // Group incoming questions by year so we can renumber per-year sequentially
        // while preserving source order
        var byYear = new Dictionary<int, List<RepoQuestionDto>>();
        foreach (var q in questions)
        {
            // Skip clearly incomplete questions — must have question text, options, and an answer
            if (string.IsNullOrWhiteSpace(q.Question) || q.Option == null || string.IsNullOrWhiteSpace(q.Answer))
                continue;

            var options = new List<string?> { q.Option.A, q.Option.B, q.Option.C, q.Option.D }
                .Where(o => !string.IsNullOrWhiteSpace(o)).ToList();
            if (options.Count < 2) continue;

            var answerLetter = q.Answer.Trim().ToUpper();
            if (answerLetter != "A" && answerLetter != "B" && answerLetter != "C" && answerLetter != "D") continue;

            int year = (int.TryParse(q.Examyear, out int py) && py >= 1990 && py <= 2030) ? py : 2000;

            if (!byYear.ContainsKey(year)) byYear[year] = new List<RepoQuestionDto>();
            byYear[year].Add(q);
        }

        var valid = new List<QuestionBank>();

        foreach (var (year, yearQuestions) in byYear)
        {
            // Get next available sequential question number for this subject+year
            int nextQNum = existingSet.Where(k => k.Year == year).Select(k => k.QuestionNumber)
                .DefaultIfEmpty(0).Max() + 1;

            foreach (var q in yearQuestions)
            {
                if (existingSet.Contains((year, nextQNum))) { nextQNum++; continue; }

                var options = new List<string?> { q.Option!.A, q.Option.B, q.Option.C, q.Option.D }
                    .Where(o => !string.IsNullOrWhiteSpace(o))
                    .Select(o => CapFirst(o!.Trim()))
                    .ToList();

                var answerLetter = q.Answer!.Trim().ToUpper();
                string correctAnswer = answerLetter switch
                {
                    "A" => options.Count >= 1 ? options[0] : "",
                    "B" => options.Count >= 2 ? options[1] : "",
                    "C" => options.Count >= 3 ? options[2] : "",
                    "D" => options.Count >= 4 ? options[3] : "",
                    _ => ""
                };
                if (string.IsNullOrWhiteSpace(correctAnswer)) { nextQNum++; continue; }

                valid.Add(new QuestionBank
                {
                    Subject = subjectClean,
                    Year = year,
                    QuestionNumber = nextQNum++,
                    Text = CapFirst(q.Question!.Trim()),
                    OptionsJson = JsonSerializer.Serialize(options),
                    CorrectAnswer = CapFirst(correctAnswer),
                    Section = string.IsNullOrWhiteSpace(q.Section) ? string.Empty : CapFirst(q.Section.Trim()),
                    ImageUrl = q.Image ?? string.Empty
                });
            }
        }

        db.QuestionBank.AddRange(valid);
        await db.SaveChangesAsync();
        return Ok(new { imported = valid.Count, skipped = questions.Count - valid.Count });
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import(List<QuestionBankCreateDto> questions)
    {
        if (questions.Count == 0) return BadRequest("No questions provided.");
        var valid = new List<QuestionBank>();
        foreach (var dto in questions)
        {
            if (string.IsNullOrWhiteSpace(dto.Text) || dto.Options is null || dto.Options.Count < 2) continue;
            var resolvedAnswer = ResolveCorrectAnswer(dto.CorrectAnswer, dto.Options);
            if (!dto.Options.Contains(resolvedAnswer, StringComparer.OrdinalIgnoreCase)) continue;
            valid.Add(new QuestionBank
            {
                Subject = dto.Subject.Trim(),
                Year = dto.Year,
                QuestionNumber = dto.QuestionNumber <= 0 ? valid.Count + 1 : dto.QuestionNumber,
                Text = dto.Text.Trim(),
                OptionsJson = JsonSerializer.Serialize(dto.Options),
                CorrectAnswer = resolvedAnswer,
                Section = dto.Section ?? string.Empty,
                ImageUrl = dto.ImageUrl ?? string.Empty
            });
        }
        db.QuestionBank.AddRange(valid);
        await db.SaveChangesAsync();
        return Ok(new { imported = valid.Count, skipped = questions.Count - valid.Count });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, QuestionBankCreateDto dto)
    {
        var q = await db.QuestionBank.FindAsync(id);
        if (q is null) return NotFound();
        q.Subject = dto.Subject.Trim();
        q.Year = dto.Year;
        q.QuestionNumber = dto.QuestionNumber;
        q.Text = dto.Text.Trim();
        q.OptionsJson = JsonSerializer.Serialize(dto.Options);
        q.CorrectAnswer = ResolveCorrectAnswer(dto.CorrectAnswer, dto.Options);
        q.Section = dto.Section ?? string.Empty;
        q.ImageUrl = dto.ImageUrl ?? string.Empty;
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var q = await db.QuestionBank.FindAsync(id);
        if (q is null) return NotFound();
        db.QuestionBank.Remove(q);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("generate-exam")]
    public async Task<IActionResult> GenerateExam(ExamGenerateDto dto)
    {
        if (dto.Subjects.Count == 0) return BadRequest("At least one subject required.");
        if (dto.Subjects.Count > 4) return BadRequest("Maximum 4 subjects allowed.");

        var exam = new Exam
        {
            Title = dto.Title,
            Subject = string.Join(", ", dto.Subjects.Select(s => s.Subject)),
            DurationMinutes = dto.DurationMinutes,
            ShuffleQuestions = dto.ShuffleQuestions,
            ShuffleOptions = dto.ShuffleOptions,
            AccessPassword = dto.AccessPassword ?? string.Empty,
            CreatedAt = DateTime.UtcNow
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        var rng = new Random();
        int qNum = 1;
        foreach (var subjectConfig in dto.Subjects)
        {
            var pool = await db.QuestionBank
                .Where(q => q.Subject == subjectConfig.Subject && subjectConfig.Years.Contains(q.Year))
                .ToListAsync();

            foreach (var bq in pool.OrderBy(_ => rng.Next()).Take(subjectConfig.QuestionCount))
            {
                db.Questions.Add(new Question
                {
                    ExamId = exam.Id,
                    QuestionNumber = qNum++,
                    Text = bq.Text,
                    OptionsJson = bq.OptionsJson,
                    CorrectAnswer = bq.CorrectAnswer
                });
            }
        }
        await db.SaveChangesAsync();
        return Ok(exam.Id);
    }

    private static string ResolveCorrectAnswer(string correctAnswer, List<string> options)
    {
        if (options is null || options.Count == 0 || string.IsNullOrWhiteSpace(correctAnswer)) return correctAnswer;
        var clean = correctAnswer.Trim().ToUpper();
        if (clean == "A" && options.Count >= 1) return options[0];
        if (clean == "B" && options.Count >= 2) return options[1];
        if (clean == "C" && options.Count >= 3) return options[2];
        if (clean == "D" && options.Count >= 4) return options[3];
        return correctAnswer;
    }
}

public class RepoQuestionDto
{
    public int Id { get; set; }
    public string? Question { get; set; }
    public RepoOptionDto? Option { get; set; }
    public string? Answer { get; set; }
    public string? Examyear { get; set; }
    public string? Examtype { get; set; }
    public string? Section { get; set; }
    public string? Image { get; set; }
    public string? Solution { get; set; }
}

public class RepoOptionDto
{
    public string? A { get; set; }
    public string? B { get; set; }
    public string? C { get; set; }
    public string? D { get; set; }
}
