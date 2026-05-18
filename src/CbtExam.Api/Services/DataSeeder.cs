using CbtExam.Data;
using CbtExam.Shared.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CbtExam.Api.Services;

public static class DataSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        // Automatically remove any default seeded general knowledge test
        var existing = await db.Exams.FirstOrDefaultAsync(e => e.Title == "General Knowledge Test" || e.Subject == "General Knowledge");
        if (existing is not null)
        {
            db.Exams.Remove(existing);
            await db.SaveChangesAsync();
        }
    }
}
