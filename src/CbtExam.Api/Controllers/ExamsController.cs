using CbtExam.Data;
using CbtExam.Shared.DTOs;
using CbtExam.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CbtExam.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ExamsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await db.Exams
            .Select(e => new ExamDto(e.Id, e.Title, e.Subject, e.DurationMinutes, e.ShuffleQuestions, e.ShuffleOptions, e.CreatedAt, e.Questions.Count))
            .ToListAsync());

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        var exam = await db.Exams.Include(e => e.Questions).FirstOrDefaultAsync(e => e.Id == id);
        return exam is null ? NotFound() : Ok(exam);
    }

    [HttpPost]
    public async Task<IActionResult> Create(ExamCreateDto dto)
    {
        var exam = new Exam
        {
            Title = dto.Title, Subject = dto.Subject,
            DurationMinutes = dto.DurationMinutes,
            ShuffleQuestions = dto.ShuffleQuestions,
            ShuffleOptions = dto.ShuffleOptions
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();
        return Ok(exam.Id);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var exam = await db.Exams.FindAsync(id);
        if (exam is null) return NotFound();
        db.Exams.Remove(exam);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id}/questions")]
    public async Task<IActionResult> AddQuestion(int id, QuestionCreateDto dto)
    {
        var exam = await db.Exams.FindAsync(id);
        if (exam is null) return NotFound();
        var q = new Question
        {
            ExamId = id,
            QuestionNumber = dto.QuestionNumber,
            Text = dto.Text,
            OptionsJson = JsonSerializer.Serialize(dto.Options),
            CorrectAnswer = dto.CorrectAnswer
        };
        db.Questions.Add(q);
        await db.SaveChangesAsync();
        return Ok(q.Id);
    }

    [HttpGet("{id}/questions")]
    public async Task<IActionResult> GetQuestions(int id) =>
        Ok(await db.Questions.Where(q => q.ExamId == id).OrderBy(q => q.QuestionNumber).ToListAsync());

    [HttpDelete("questions/{questionId}")]
    public async Task<IActionResult> DeleteQuestion(int questionId)
    {
        var q = await db.Questions.FindAsync(questionId);
        if (q is null) return NotFound();
        db.Questions.Remove(q);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
