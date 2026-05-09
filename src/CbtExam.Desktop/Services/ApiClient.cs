using CbtExam.Shared.DTOs;
using System.Net;
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
    private bool IsReady => _http.BaseAddress is not null;

    public void SetBaseUrl(string url)
    {
        _http.BaseAddress = new Uri(url + "/");
        _http.DefaultRequestHeaders.Remove("X-Admin-Key");
        _http.DefaultRequestHeaders.Add("X-Admin-Key", Environment.GetEnvironmentVariable("CBT_ADMIN_KEY") ?? "admin123");
    }

    // Exams
    public Task<List<ExamDto>?> GetExamsAsync() => IsReady ? _http.GetFromJsonAsync<List<ExamDto>>("api/exams") : Task.FromResult<List<ExamDto>?>([]);
    public Task<HttpResponseMessage> CreateExamAsync(ExamCreateDto dto) => IsReady ? _http.PostAsJsonAsync("api/exams", dto) : OfflineResponse();
    public Task<HttpResponseMessage> DeleteExamAsync(int id) => IsReady ? _http.DeleteAsync($"api/exams/{id}") : OfflineResponse();
    public Task<HttpResponseMessage> AddQuestionAsync(int examId, QuestionCreateDto dto) => IsReady ? _http.PostAsJsonAsync($"api/exams/{examId}/questions", dto) : OfflineResponse();
    public Task<HttpResponseMessage> ImportQuestionsAsync(int examId, List<QuestionCreateDto> dto) => IsReady ? _http.PostAsJsonAsync($"api/exams/{examId}/questions/import", dto) : OfflineResponse();
    public Task<HttpResponseMessage> DeleteQuestionAsync(int questionId) => IsReady ? _http.DeleteAsync($"api/exams/questions/{questionId}") : OfflineResponse();

    // Sessions
    public Task<List<SessionDto>?> GetSessionsAsync() => IsReady ? _http.GetFromJsonAsync<List<SessionDto>>("api/sessions") : Task.FromResult<List<SessionDto>?>([]);
    public Task<HttpResponseMessage> StartSessionAsync(int examId) => IsReady ? _http.PostAsJsonAsync("api/sessions/start", new SessionStartDto(examId)) : OfflineResponse();
    public Task<HttpResponseMessage> StopSessionAsync(int sessionId) => IsReady ? _http.PostAsync($"api/sessions/{sessionId}/stop", content: null) : OfflineResponse();
    public Task<HttpResponseMessage> EndAllSessionsAsync() => IsReady ? _http.PostAsync("api/sessions/end-all", content: null) : OfflineResponse();
    public Task<HttpResponseMessage> ExportSnapshotAsync() => IsReady ? _http.PostAsync("api/sessions/export", content: null) : OfflineResponse();
    public Task<List<StudentStatusDto>?> GetStudentsAsync(int sessionId) => IsReady ? _http.GetFromJsonAsync<List<StudentStatusDto>>($"api/sessions/{sessionId}/students") : Task.FromResult<List<StudentStatusDto>?>([]);
    public Task<List<ResultDto>?> GetResultsAsync(int sessionId) => IsReady ? _http.GetFromJsonAsync<List<ResultDto>>($"api/sessions/{sessionId}/results") : Task.FromResult<List<ResultDto>?>([]);

    // Students admin
    public Task<List<StudentAdminDto>?> GetStudentRosterAsync() => IsReady ? _http.GetFromJsonAsync<List<StudentAdminDto>>("api/students") : Task.FromResult<List<StudentAdminDto>?>([]);
    public Task<HttpResponseMessage> UpsertStudentAsync(StudentUpsertDto dto) => IsReady ? _http.PostAsJsonAsync("api/students", dto) : OfflineResponse();
    public Task<HttpResponseMessage> DeleteStudentAsync(int id) => IsReady ? _http.DeleteAsync($"api/students/{id}") : OfflineResponse();
    public Task<HttpResponseMessage> UpdateStudentPasswordAsync(StudentPasswordUpdateDto dto) => IsReady ? _http.PostAsJsonAsync("api/students/password", dto) : OfflineResponse();

    private static Task<HttpResponseMessage> OfflineResponse() =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            ReasonPhrase = "Server not started"
        });
}
