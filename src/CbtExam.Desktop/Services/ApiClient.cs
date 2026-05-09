using CbtExam.Shared.DTOs;
using System.Net.Http;
using System.Net.Http.Json;

namespace CbtExam.Desktop.Services;

/// <summary>
/// Typed HTTP client used by WPF ViewModels to call the embedded API.
/// </summary>
public class ApiClient
{
    private readonly HttpClient _http = new();
    public string BaseUrl => _http.BaseAddress?.ToString().TrimEnd('/') ?? string.Empty;

    public void SetBaseUrl(string url) => _http.BaseAddress = new Uri(url + "/");

    // Exams
    public Task<List<ExamDto>?> GetExamsAsync() => _http.GetFromJsonAsync<List<ExamDto>>("api/exams");
    public Task<HttpResponseMessage> CreateExamAsync(ExamCreateDto dto) => _http.PostAsJsonAsync("api/exams", dto);
    public Task<HttpResponseMessage> DeleteExamAsync(int id) => _http.DeleteAsync($"api/exams/{id}");
    public Task<HttpResponseMessage> AddQuestionAsync(int examId, QuestionCreateDto dto) => _http.PostAsJsonAsync($"api/exams/{examId}/questions", dto);
    public Task<HttpResponseMessage> DeleteQuestionAsync(int questionId) => _http.DeleteAsync($"api/exams/questions/{questionId}");

    // Sessions
    public Task<List<SessionDto>?> GetSessionsAsync() => _http.GetFromJsonAsync<List<SessionDto>>("api/sessions");
    public Task<HttpResponseMessage> StartSessionAsync(int examId) => _http.PostAsJsonAsync("api/sessions/start", new SessionStartDto(examId));
    public Task<HttpResponseMessage> StopSessionAsync(int sessionId) => _http.DeleteAsync($"api/sessions/{sessionId}/stop");
    public Task<List<StudentStatusDto>?> GetStudentsAsync(int sessionId) => _http.GetFromJsonAsync<List<StudentStatusDto>>($"api/sessions/{sessionId}/students");
    public Task<List<ResultDto>?> GetResultsAsync(int sessionId) => _http.GetFromJsonAsync<List<ResultDto>>($"api/sessions/{sessionId}/results");
}
