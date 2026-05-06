using Serilog;

namespace Web3270.Server.Configuration;

public static class LoggerConfig
{
    public static void AddCustomSerilog(this WebApplicationBuilder builder)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .ReadFrom.Configuration(builder.Configuration)
            .WriteTo.Console(
                outputTemplate:
                "{Timestamp:yy-MM-dd HH:mm:ss zzz} CorrelationId={CorrelationId} [{Level}] {Message}{NewLine}{Exception}")
            .CreateLogger();

        builder.Host.UseSerilog(Log.Logger, true);
    }
}