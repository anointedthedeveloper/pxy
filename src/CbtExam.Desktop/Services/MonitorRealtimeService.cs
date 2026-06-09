using CbtExam.Shared.DTOs;
using Microsoft.AspNetCore.SignalR.Client;

namespace CbtExam.Desktop.Services;

public sealed class MonitorRealtimeService
{
    private HubConnection? _connection;
    private string _currentSessionCode = string.Empty;

    public event Action<IReadOnlyList<StudentStatusDto>>? StudentUpdated;
    public event Action? SessionStarted;
    public event Action? SessionEnded;

    public async Task ConnectAsync(string baseUrl, string sessionCode, CancellationToken cancellationToken = default)
    {
        if (_currentSessionCode == sessionCode && _connection?.State == HubConnectionState.Connected)
            return;

        await DisconnectAsync();
        _currentSessionCode = sessionCode;

        _connection = new HubConnectionBuilder()
            .WithUrl($"{baseUrl.TrimEnd('/')}/hubs/exam")
            .WithAutomaticReconnect()
            .Build();

        _connection.On<List<StudentStatusDto>>("StudentUpdated", payload =>
            StudentUpdated?.Invoke(payload));

        _connection.On("ExamStarted", () =>
            SessionStarted?.Invoke());

        _connection.On("SessionEnded", () =>
            SessionEnded?.Invoke());

        _connection.Reconnected += async _ =>
        {
            await _connection.SendAsync("JoinAdminGroup", _currentSessionCode);
        };

        await _connection.StartAsync(cancellationToken);
        await _connection.SendAsync("JoinAdminGroup", sessionCode, cancellationToken);
    }

    public async Task DisconnectAsync()
    {
        if (_connection is null) return;
        await _connection.DisposeAsync();
        _connection = null;
        _currentSessionCode = string.Empty;
    }
}
