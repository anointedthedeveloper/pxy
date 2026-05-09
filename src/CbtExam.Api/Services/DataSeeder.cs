using CbtExam.Data;
using CbtExam.Shared.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CbtExam.Api.Services;

public static class DataSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Exams.AnyAsync()) return; // Already seeded

        var exam = new Exam
        {
            Title = "General Knowledge Test",
            Subject = "General Knowledge",
            DurationMinutes = 30,
            ShuffleQuestions = true,
            ShuffleOptions = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        var questions = new[]
        {
            new Question { ExamId = exam.Id, QuestionNumber = 1, Text = "What is the capital of France?",
                OptionsJson = JsonSerializer.Serialize(new[]{"London","Berlin","Paris","Madrid"}), CorrectAnswer = "Paris" },
            new Question { ExamId = exam.Id, QuestionNumber = 2, Text = "Which planet is known as the Red Planet?",
                OptionsJson = JsonSerializer.Serialize(new[]{"Venus","Mars","Jupiter","Saturn"}), CorrectAnswer = "Mars" },
            new Question { ExamId = exam.Id, QuestionNumber = 3, Text = "What is 2 + 2 × 2?",
                OptionsJson = JsonSerializer.Serialize(new[]{"8","6","4","10"}), CorrectAnswer = "6" },
            new Question { ExamId = exam.Id, QuestionNumber = 4, Text = "Who wrote 'Romeo and Juliet'?",
                OptionsJson = JsonSerializer.Serialize(new[]{"Charles Dickens","William Shakespeare","Jane Austen","Mark Twain"}), CorrectAnswer = "William Shakespeare" },
            new Question { ExamId = exam.Id, QuestionNumber = 5, Text = "What is the chemical symbol for water?",
                OptionsJson = JsonSerializer.Serialize(new[]{"O2","H2O","CO2","NaCl"}), CorrectAnswer = "H2O" },
            new Question { ExamId = exam.Id, QuestionNumber = 6, Text = "How many continents are there on Earth?",
                OptionsJson = JsonSerializer.Serialize(new[]{"5","6","7","8"}), CorrectAnswer = "7" },
            new Question { ExamId = exam.Id, QuestionNumber = 7, Text = "What is the largest ocean on Earth?",
                OptionsJson = JsonSerializer.Serialize(new[]{"Atlantic","Indian","Arctic","Pacific"}), CorrectAnswer = "Pacific" },
            new Question { ExamId = exam.Id, QuestionNumber = 8, Text = "In which year did World War II end?",
                OptionsJson = JsonSerializer.Serialize(new[]{"1943","1944","1945","1946"}), CorrectAnswer = "1945" },
            new Question { ExamId = exam.Id, QuestionNumber = 9, Text = "What is the speed of light (approx)?",
                OptionsJson = JsonSerializer.Serialize(new[]{"300,000 km/s","150,000 km/s","450,000 km/s","200,000 km/s"}), CorrectAnswer = "300,000 km/s" },
            new Question { ExamId = exam.Id, QuestionNumber = 10, Text = "Which element has the atomic number 1?",
                OptionsJson = JsonSerializer.Serialize(new[]{"Helium","Oxygen","Hydrogen","Carbon"}), CorrectAnswer = "Hydrogen" },
        };

        db.Questions.AddRange(questions);
        await db.SaveChangesAsync();
    }
}
