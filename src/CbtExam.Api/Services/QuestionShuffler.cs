using CbtExam.Shared.DTOs;
using CbtExam.Shared.Models;
using System.Text.Json;

namespace CbtExam.Api.Services;

public static class QuestionShuffler
{
    private static readonly JsonSerializerOptions _json = new();

    /// <summary>
    /// Shuffles options and returns the new correct index.
    /// The correct answer identity is preserved by value, not position.
    /// </summary>
    public static ShuffledQuestionDto Shuffle(Question q, bool shuffleOptions, Random? rng = null)
    {
        var options = JsonSerializer.Deserialize<List<string>>(q.OptionsJson, _json) ?? [];
        rng ??= Random.Shared;

        if (!shuffleOptions)
        {
            var idx = options.IndexOf(q.CorrectAnswer);
            return new ShuffledQuestionDto(q.Id, q.QuestionNumber, q.Text, options, idx < 0 ? 0 : idx, q.Subject ?? "", q.Section ?? "", q.ImageUrl ?? "");
        }

        // Fisher-Yates shuffle on a copy
        var shuffled = options.ToList();
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        var correctIndex = shuffled.IndexOf(q.CorrectAnswer);
        return new ShuffledQuestionDto(q.Id, q.QuestionNumber, q.Text, shuffled, correctIndex < 0 ? 0 : correctIndex, q.Subject ?? "", q.Section ?? "", q.ImageUrl ?? "");
    }

    public static List<ShuffledQuestionDto> ShuffleAll(IEnumerable<Question> questions, bool shuffleQuestions, bool shuffleOptions, int? seed = null)
    {
        var list = questions.ToList();
        var rng = seed.HasValue ? new Random(seed.Value) : Random.Shared;
        if (shuffleQuestions)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            // Re-number after shuffle
            for (int i = 0; i < list.Count; i++) list[i].QuestionNumber = i + 1;
        }
        return list.Select(q => Shuffle(q, shuffleOptions, rng)).ToList();
    }
}
