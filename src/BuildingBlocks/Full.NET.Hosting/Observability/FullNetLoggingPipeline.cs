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
        Action<LoggerAuditSinkConfiguration> configureHighPrioritySink,
        Action<LoggerSinkConfiguration>? configureGeneralWriteTo = null,
        Action<LoggerSinkConfiguration>? configureHighPriorityWriteTo = null)
    {
        configuration
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", applicationName);

        var generalSink = CreateSink(configureGeneralSink, configureGeneralWriteTo);
        try
        {
            var highPrioritySink = CreateSink(
                configureHighPrioritySink,
                configureHighPriorityWriteTo);
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
        Action<LoggerAuditSinkConfiguration> configureAuditSink,
        Action<LoggerSinkConfiguration>? configureWriteTo = null)
    {
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Verbose();
        configureAuditSink(configuration.AuditTo);
        configureWriteTo?.Invoke(configuration.WriteTo);
        return configuration.CreateLogger();
    }
}
