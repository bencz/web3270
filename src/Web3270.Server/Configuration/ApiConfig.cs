using Web3270.Server.Services;

namespace Web3270.Server.Configuration;

public static class ApiConfig
{
    private const string ApplicationName = "Web3270";

    public static void AddAServerConfiguration(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<Tn3270Options>(
            builder.Configuration.GetSection(Tn3270Options.SectionName));

        builder.Services.AddSignalR(options => { options.MaximumReceiveMessageSize = 1024 * 1024; });
        builder.Services.AddSingleton<ITerminalSessionManager, TerminalSessionManager>();
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy => policy
                .SetIsOriginAllowed(_ => true)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
        });
    }

    public static void ConfigureHealthChecks(this WebApplication app)
    {
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
    }

    public static void MapRoutes(this WebApplication app)
    {
        app.MapGet("/traces", () =>
        {
            var dir = Path.Combine(app.Environment.ContentRootPath, "traces");
            if (!Directory.Exists(dir))
                return Results.Json(Array.Empty<object>());

            var files = new DirectoryInfo(dir)
                .GetFiles("*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => new { name = f.Name, size = f.Length, modified = f.LastWriteTimeUtc });

            return Results.Json(files);
        });

        app.MapGet("/traces/{name}", (string name) =>
        {
            var dir = Path.Combine(app.Environment.ContentRootPath, "traces");
            var safe = Path.GetFileName(name);
            var full = Path.Combine(dir, safe);
            return !File.Exists(full)
                ? Results.NotFound()
                : Results.File(full, "text/plain", safe);
        });
    }

    public static void RunApplication(this WebApplication app)
    {
        try
        {
            app.Logger.LogInformation("Starting web host ({ApplicationName})...", ApplicationName);
            app.Run();
        }
        catch (Exception ex)
        {
            app.Logger.LogCritical(ex, "Host terminated unexpectedly ({ApplicationName})...", ApplicationName);
        }
        finally
        {
            Serilog.Log.CloseAndFlush();
        }
    }
}