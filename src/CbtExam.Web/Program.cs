using CbtExam.Api;
using System.IO;

// When running via `dotnet run`, the working directory is the project directory.
// Walk up to find the repo root (where wwwroot and database folders live).
static string FindRepoRoot()
{
    var dir = Directory.GetCurrentDirectory();
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir, "CbtExam.sln")))
            return dir;
        dir = Directory.GetParent(dir)?.FullName;
    }
    // Fallback to current directory
    return Directory.GetCurrentDirectory();
}

var repoRoot = FindRepoRoot();

var dbPath = Path.Combine(repoRoot, "database", "cbt_exam.db");
var wwwrootPath = Path.Combine(repoRoot, "wwwroot");

Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
Directory.CreateDirectory(wwwrootPath);

Console.WriteLine($"[CbtExam.Web] Repo root: {repoRoot}");
Console.WriteLine($"[CbtExam.Web] DB path:   {dbPath}");
Console.WriteLine($"[CbtExam.Web] wwwroot:   {wwwrootPath}");

var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var p) ? p : 5000;

var app = await ApiBootstrap.BuildApp(dbPath, wwwrootPath, port);
await app.RunAsync();
