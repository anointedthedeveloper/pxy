using CbtExam.Api.Hubs;
using CbtExam.Data;
using CbtExam.Shared.DTOs;
using CbtExam.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CbtExam.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StudentsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await db.Students
            .OrderBy(s => s.FullName)
            .Select(s => new StudentAdminDto(s.Id, s.FullName, s.StudentId, s.IsActive, s.Password))
            .ToListAsync());

    [HttpPost]
    public async Task<IActionResult> Upsert(StudentUpsertDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.FullName))
            return BadRequest("Full name is required.");

        Student? entity = null;
        if (dto.Id.HasValue)
            entity = await db.Students.FindAsync(dto.Id.Value);

        // Auto-generate StudentId if not provided
        var studentId = dto.StudentId?.Trim();
        if (string.IsNullOrWhiteSpace(studentId))
        {
            studentId = await GenerateUniqueUsernameAsync();
        }

        // Check for duplicate username (case-insensitive) on new records or on ID change
        if (entity is null || !string.Equals(entity.StudentId, studentId, StringComparison.OrdinalIgnoreCase))
        {
            var allStudents = await db.Students.ToListAsync();
            bool duplicate = allStudents.Any(s =>
                (entity is null || s.Id != entity.Id) &&
                s.StudentId.Equals(studentId, StringComparison.OrdinalIgnoreCase));
            if (duplicate)
                return BadRequest($"Username '{studentId}' already exists (usernames are case-insensitive).");
        }

        // Check for duplicate full name (case-insensitive) to prevent two accounts with same name differing only by case
        {
            var allStudents = await db.Students.ToListAsync();
            bool dupName = allStudents.Any(s =>
                (entity is null || s.Id != entity.Id) &&
                s.FullName.Equals(dto.FullName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                s.StudentId.Equals(studentId, StringComparison.OrdinalIgnoreCase));
            if (dupName)
                return BadRequest($"A student with name '{dto.FullName.Trim()}' and username '{studentId}' already exists.");
        }

        if (entity is null)
        {
            entity = new Student
            {
                FullName  = dto.FullName.Trim(),
                StudentId = studentId,
                IsActive  = dto.IsActive,
                Password  = string.IsNullOrWhiteSpace(dto.Password) ? "1234" : dto.Password.Trim()
            };
            db.Students.Add(entity);
        }
        else
        {
            entity.FullName  = dto.FullName.Trim();
            entity.StudentId = studentId;
            entity.IsActive  = dto.IsActive;
            if (!string.IsNullOrWhiteSpace(dto.Password))
                entity.Password = dto.Password.Trim();
        }

        await db.SaveChangesAsync();
        return Ok(new StudentAdminDto(entity.Id, entity.FullName, entity.StudentId, entity.IsActive, entity.Password));
    }

    private static readonly char[] _usernameChars = "abcdefghijklmnopqrstuvwxyz".ToCharArray();
    private static readonly Random _rng = new();

    private async Task<string> GenerateUniqueUsernameAsync()
    {
        var existing = await db.Students.Select(s => s.StudentId).ToListAsync();
        var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        for (int attempt = 0; attempt < 1000; attempt++)
        {
            // Format: 1-4 digit number + 2 random lowercase letters, e.g. "001ab", "042xy", "1000aq"
            int num = _rng.Next(1, 10000);
            char c1 = _usernameChars[_rng.Next(_usernameChars.Length)];
            char c2 = _usernameChars[_rng.Next(_usernameChars.Length)];
            string candidate = $"{num:D3}{c1}{c2}";
            if (!existingSet.Contains(candidate))
                return candidate;
        }

        // Extremely unlikely fallback
        return Guid.NewGuid().ToString("N")[..8];
    }

    [HttpPost("password")]
    public async Task<IActionResult> UpdatePassword(StudentPasswordUpdateDto dto)
    {
        var student = await db.Students.FindAsync(dto.StudentId);
        if (student is null) return NotFound();
        if (string.IsNullOrWhiteSpace(dto.NewPassword)) return BadRequest("Password cannot be empty.");
        student.Password = dto.NewPassword.Trim();
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var student = await db.Students.FindAsync(id);
        if (student is null) return NotFound();
        db.Students.Remove(student);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // POST /api/students/{id}/logout — force-clears the active session lock for one student
    [HttpPost("{id}/logout")]
    public async Task<IActionResult> ForceLogout(int id)
    {
        StudentController._activeSessions.TryRemove(id, out _);
        
        // Send SignalR notification to force logout the student's browser
        try
        {
            var hub = HttpContext.RequestServices.GetRequiredService<IHubContext<ExamHub>>();
            await hub.Clients.All.SendAsync("ForceLogout");
        }
        catch (Exception ex)
        {
            // Log but don't fail the logout if SignalR fails
            Console.WriteLine($"SignalR ForceLogout failed: {ex.Message}");
        }
        
        return Ok(new { message = "Session cleared." });
    }

    // POST /api/students/logout-all — force-clears all active session locks
    [HttpPost("logout-all")]
    public async Task<IActionResult> ForceLogoutAll()
    {
        StudentController._activeSessions.Clear();
        
        // Send SignalR notification to force logout all students' browsers
        try
        {
            var hub = HttpContext.RequestServices.GetRequiredService<IHubContext<ExamHub>>();
            await hub.Clients.All.SendAsync("ForceLogout");
        }
        catch (Exception ex)
        {
            // Log but don't fail the logout if SignalR fails
            Console.WriteLine($"SignalR ForceLogout failed: {ex.Message}");
        }
        
        return Ok(new { message = "All sessions cleared." });
    }
}
