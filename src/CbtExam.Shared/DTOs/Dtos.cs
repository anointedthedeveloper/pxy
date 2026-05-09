namespace CbtExam.Shared.DTOs;

// --- Exam DTOs ---
public record ExamCreateDto(string Title, string Subject, int DurationMinutes, bool ShuffleQuestions, bool ShuffleOptions);
public record ExamDto(int Id, string Title, string Subject, int DurationMinutes, bool ShuffleQuestions, bool ShuffleOptions, DateTime CreatedAt, int QuestionCount);

// --- Question DTOs ---
public record QuestionCreateDto(int QuestionNumber, string Text, List<string> Options, string CorrectAnswer);

// --- Session DTOs ---
public record SessionStartDto(int ExamId);
public record SessionDto(int Id, int ExamId, string ExamTitle, string SessionCode, DateTime StartedAt, bool IsActive, int StudentCount);

// --- Student DTOs ---
public record JoinExamDto(string SessionCode, string FullName, string StudentId);
public record JoinResultDto(int StudentExamId, int SessionId, int ExamId, string ExamTitle, int DurationMinutes);

// --- Question for student (shuffled) ---
public record ShuffledQuestionDto(int QuestionId, int QuestionNumber, string Text, List<string> Options, int CorrectIndex);

// --- Answer submission ---
public record AnswerSubmitDto(int QuestionId, string SelectedAnswer);
public record ExamSubmitDto(int StudentExamId, List<AnswerSubmitDto> Answers);
public record SubmitResultDto(int Score, int Total, double Percentage);

// --- Monitor ---
public record StudentStatusDto(int StudentExamId, string FullName, string StudentId, DateTime JoinedAt, bool IsSubmitted, int TabSwitchCount, int AnsweredCount);

// --- Results ---
public record ResultDto(int StudentExamId, string FullName, string StudentId, int Score, int Total, double Percentage, DateTime? SubmittedAt);

// --- Activity log ---
public record ActivityLogDto(int StudentExamId, string Activity, DateTime Timestamp);

// --- Tab switch report ---
public record TabSwitchDto(int StudentExamId);
