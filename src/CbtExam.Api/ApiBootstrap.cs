using CbtExam.Api.Hubs;
using CbtExam.Api.Services;
using CbtExam.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.IO;
using Microsoft.AspNetCore.Rewrite;

namespace CbtExam.Api;

public static class ApiBootstrap
{
    public static async Task<WebApplication> BuildApp(string dbPath, string wwwrootPath, int port = 7031)
    {
        // Empty args + suppress default console/hosting noise inside WPF
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args        = [],
            WebRootPath = wwwrootPath,
            ContentRootPath = wwwrootPath
        });

        // Silence ASP.NET Core's own console logging (WPF has no console)
        builder.Logging.ClearProviders();

        builder.WebHost.UseKestrel(k =>
        {
            k.ListenAnyIP(port);
            // Configure Kestrel to limit request size to prevent large payload attacks
            k.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10MB max
            // Configure timeouts to prevent slowloris attacks
            k.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(5);
            k.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
        });

        var dbDirectory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(dbDirectory))
            Directory.CreateDirectory(dbDirectory);

        builder.Services.AddDbContext<AppDbContext>(o =>
            o.UseSqlite($"Data Source={dbPath};Cache=Shared;Pooling=True"));

        builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
            p.SetIsOriginAllowed(_ => true)
             .AllowAnyMethod()
             .AllowAnyHeader()
             .AllowCredentials()));

        builder.Services.AddControllers()
            .AddApplicationPart(typeof(ApiBootstrap).Assembly);
        builder.Services.AddSignalR(options => {
            options.MaximumReceiveMessageSize = 32768;
            options.EnableDetailedErrors = true;
        }).AddHubOptions<ExamHub>(options => {
            options.EnableDetailedErrors = false; // Disable detailed errors in production for security
        });
        builder.Services.AddScoped<SnapshotExportService>();

        var app = builder.Build();
        var adminKey = Environment.GetEnvironmentVariable("CBT_ADMIN_KEY") ?? "admin123";

        // Auto-migrate on startup — robust reset on schema mismatch
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            bool created = false;
            try
            {
                db.Database.EnsureCreated();
                
                // Enable Write-Ahead Logging for high concurrency (500+ users)
                db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
                
                // Patch any columns added after initial EnsureCreated (safe on existing DBs)
                try
                {
                    var connection = db.Database.GetDbConnection();
                    await connection.OpenAsync();

                    async Task AddColumnIfMissing(string table, string column, string definition)
                    {
                        using var cmd = connection.CreateCommand();
                        cmd.CommandText = $"PRAGMA table_info({table})";
                        using var rdr = await cmd.ExecuteReaderAsync();
                        bool exists = false;
                        while (await rdr.ReadAsync())
                            if (string.Equals(rdr.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                            { exists = true; break; }
                        if (!exists)
                        {
                            using var alter = connection.CreateCommand();
                            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
                            await alter.ExecuteNonQueryAsync();
                        }
                    }

                    // ExamSessions columns
                    await AddColumnIfMissing("ExamSessions", "CustomSessionName", "TEXT DEFAULT ''");
                    await AddColumnIfMissing("ExamSessions", "IsStarted",         "INTEGER DEFAULT 0");
                    await AddColumnIfMissing("ExamSessions", "AutoApprove",        "INTEGER DEFAULT 1");
                    await AddColumnIfMissing("ExamSessions", "AllowRetakes",       "INTEGER DEFAULT 0");

                    // StudentExams columns
                    await AddColumnIfMissing("StudentExams", "IsApproved",  "INTEGER DEFAULT 1");
                    await AddColumnIfMissing("StudentExams", "IsRejected",  "INTEGER DEFAULT 0");
                    await AddColumnIfMissing("StudentExams", "DeviceId",    "TEXT DEFAULT ''");
                    await AddColumnIfMissing("StudentExams", "DeviceName",  "TEXT DEFAULT ''");

                    // Questions columns
                    await AddColumnIfMissing("Questions", "Subject",  "TEXT DEFAULT ''");
                    await AddColumnIfMissing("Questions", "Year",      "INTEGER DEFAULT 0");
                    await AddColumnIfMissing("Questions", "Section",   "TEXT DEFAULT ''");
                    await AddColumnIfMissing("Questions", "ImageUrl",  "TEXT DEFAULT ''");

                    // QuestionBank columns
                    await AddColumnIfMissing("QuestionBank", "Section",   "TEXT DEFAULT ''");
                    await AddColumnIfMissing("QuestionBank", "ImageUrl",  "TEXT DEFAULT ''");
                    await AddColumnIfMissing("QuestionBank", "SourceId",  "INTEGER NULL");

                    await connection.CloseAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Schema patch error: {ex.Message}");
                }
                
                // Verify schema is usable by probing known tables
                _ = db.Students.Count();
                _ = db.Exams.Count();
                created = true;
            }
            catch { }

            if (!created)
            {
                // Schema mismatch — close all connections, delete DB, recreate
                try { db.Database.CloseConnection(); } catch { }
                db.Dispose();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }

                // Fresh context after delete
                using var scope2 = app.Services.CreateScope();
                var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
                db2.Database.EnsureCreated();
                await DataSeeder.SeedAsync(db2);
                goto skipSeed;
            }
            await DataSeeder.SeedAsync(db);
            skipSeed:;
        }

        app.UseCors();
        
        // URL rewriting to remove .html extensions
        var rewriteOptions = new RewriteOptions()
            .AddRewrite("^selection$", "selection.html", skipRemainingRules: true)
            .AddRewrite("^exam$", "exam.html", skipRemainingRules: true)
            .AddRewrite("^results$", "results.html", skipRemainingRules: true)
            .AddRewrite("^waiting$", "waiting.html", skipRemainingRules: true)
            .AddRewrite("^index$", "index.html", skipRemainingRules: true);
        app.UseRewriter(rewriteOptions);
        
        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path.Value?.ToLower() ?? "";
            
            // Allow public access to certain endpoints
            if (!path.StartsWith("/api/exams") &&
                !path.StartsWith("/api/sessions"))
            {
                await next();
                return;
            }

            // Allow students to GET exams and sessions publicly
            if ((path.StartsWith("/api/exams") || path.StartsWith("/api/sessions")) && ctx.Request.Method == "GET")
            {
                await next();
                return;
            }

            // Allow student endpoints to be publicly accessible
            if (path.StartsWith("/api/student"))
            {
                await next();
                return;
            }

            if (ctx.Request.Headers.TryGetValue("X-Admin-Key", out var provided) &&
                string.Equals(provided.ToString(), adminKey, StringComparison.Ordinal))
            {
                await next();
                return;
            }

            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"error\": \"Unauthorized admin request.\"}");
        });
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapControllers();
        app.MapHub<ExamHub>("/hubs/exam");

        // SPA fallback — serve index.html (login page) for unknown GET routes only.
        // Must be GET-only so POST/PUT/DELETE to unknown paths return 404
        // instead of 405 from the static file middleware.
        app.MapFallback(async ctx =>
        {
            if (ctx.Request.Method == "GET")
            {
                ctx.Response.ContentType = "text/html";
                await ctx.Response.SendFileAsync(Path.Combine(wwwrootPath, "index.html"));
            }
            else
            {
                ctx.Response.StatusCode = 404;
            }
        });

        return app;
    }
}
