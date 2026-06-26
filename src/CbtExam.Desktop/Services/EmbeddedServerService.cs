using CbtExam.Api;
using System.Net;
using System.Net.Sockets;

namespace CbtExam.Desktop.Services;

public class EmbeddedServerService
{
    private WebApplication? _app;
    public bool IsRunning { get; private set; }
    public string ServerUrl { get; private set; } = string.Empty;

    public async Task StartAsync(string dbPath, string wwwrootPath, int port = 7031)
    {
        if (IsRunning) return;

        // Tell ASP.NET Core it's running in Production so it doesn't
        // look for appsettings.Development.json or other dev-only files
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT",     "Production");

        int currentPort = port;
        int retries = 20;
        bool started = false;

        while (!started && retries > 0)
        {
            try
            {
                _app = await ApiBootstrap.BuildApp(dbPath, wwwrootPath, currentPort);
                await _app.StartAsync();
                port = currentPort;
                started = true;
            }
            catch (Exception ex) when (ex.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase) ||
                                       ex.InnerException is SocketException or System.IO.IOException ||
                                       ex is SocketException or System.IO.IOException)
            {
                currentPort++;
                retries--;
                if (retries == 0) throw;
            }
        }

        ServerUrl = $"http://{GetLocalIp()}:{port}";
        IsRunning = true;
    }

    public async Task StopAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }
        IsRunning = false;
        ServerUrl = string.Empty;
    }

    public static string GetLocalIp()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }
}
