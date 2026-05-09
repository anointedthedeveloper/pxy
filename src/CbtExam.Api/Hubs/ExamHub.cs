using Microsoft.AspNetCore.SignalR;

namespace CbtExam.Api.Hubs;

public class ExamHub : Hub
{
    // Admin joins a group to receive live updates for a session
    public async Task JoinAdminGroup(string sessionCode) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, $"admin_{sessionCode}");

    // Called by server-side code to push student status updates
    public static async Task NotifyStudentUpdate(IHubContext<ExamHub> hub, string sessionCode, object payload) =>
        await hub.Clients.Group($"admin_{sessionCode}").SendAsync("StudentUpdated", payload);
}
