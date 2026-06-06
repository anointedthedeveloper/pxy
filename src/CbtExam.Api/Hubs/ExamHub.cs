using Microsoft.AspNetCore.SignalR;

namespace CbtExam.Api.Hubs;

public class ExamHub : Hub
{
    public async Task JoinAdminGroup(string sessionCode) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, $"admin_{sessionCode}");

    public async Task JoinStudentGroup(string sessionCode) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, $"students_{sessionCode}");

    public static async Task NotifyStudentUpdate(IHubContext<ExamHub> hub, string sessionCode, object payload) =>
        await hub.Clients.Group($"admin_{sessionCode}").SendAsync("StudentUpdated", payload);

    // Exam started — students receive decryption key trigger
    public static async Task NotifyExamStarted(IHubContext<ExamHub> hub, string sessionCode) =>
        await hub.Clients.Group($"students_{sessionCode}").SendAsync("ExamStarted");

    // Session ended by admin
    public static async Task NotifySessionEnded(IHubContext<ExamHub> hub, string sessionCode) =>
        await hub.Clients.Group($"students_{sessionCode}").SendAsync("SessionEnded");

    // Force submit all students in a session
    public static async Task NotifyForceSubmit(IHubContext<ExamHub> hub, string sessionCode) =>
        await hub.Clients.Group($"students_{sessionCode}").SendAsync("ForceSubmit");
}
