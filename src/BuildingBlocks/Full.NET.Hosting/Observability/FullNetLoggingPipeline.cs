using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace Full.NET.Hosting.Observability;

internal static class FullNetLoggingPipeline
{
    public static LoggerConfiguration Configure(
        LoggerConfiguration configuration,
        string applicationName,
        LoggingOptions options,
        FullNetLoggingMonitors monitors,
        Action<LoggerAuditSinkConfiguration> configureGeneralSink,
        Action<LoggerAuditSinkConfiguration> configureHighPrioritySink)
    {
        configuration
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", applicationName);

        var generalSink = CreateSink(configureGeneralSink);
        try
        {
            var highPrioritySink = CreateSink(configureHighPrioritySink);
            configuration.WriteTo.Sink(
                new FullNetLoggingPipelineSink(
                    generalSink,
                    highPrioritySink,
                    options,
                    monitors));
        }
        catch
        {
            if (generalSink is IDisposable disposable)
            {
                disposable.Dispose();
            }

            throw;
        }

        return configuration;
    }

    private static ILogEventSink CreateSink(
        Action<LoggerAuditSinkConfiguration> configureSink)
    {
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Verbose();
        configureSink(configuration.AuditTo);
        return configuration.CreateLogger();
    }
}
