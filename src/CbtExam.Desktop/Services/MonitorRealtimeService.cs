using CbtExam.Shared.DTOs;
using Microsoft.AspNetCore.SignalR.Client;

namespace CbtExam.Desktop.Services;

public sealed class MonitorRealtimeService
{
    private HubConnection? _connection;

    public event Action<IReadOnlyList<StudentStatusDto>>? StudentUpdated;

    public async Task ConnectAsync(string baseUrl, string sessionCode, CancellationToken cancellationToken = default)
    {
        await DisconnectAsync();
        _connection = new HubConnectionBuilder()
            .WithUrl($"{baseUrl.TrimEnd('/')}/hubs/exam")
            .WithAutomaticReconnect()
            .Build();

        _connection.On<List<StudentStatusDto>>("StudentUpdated", payload =>
        {
            StudentUpdated?.Invoke(payload);
        });

        await _connection.StartAsync(cancellationToken);
        await _connection.SendAsync("JoinAdminGroup", sessionCode, cancellationToken);
    }

    public async Task DisconnectAsync()
    {
        if (_connection is null) return;
        await _connection.DisposeAsync();
        _connection = null;
    }
}
