using Serilog;
using Serilog.Configuration;
using Serilog.Events;

namespace Full.NET.Hosting.Observability;

internal static class FullNetLoggingPipeline
{
    public static LoggerConfiguration Configure(
        LoggerConfiguration configuration,
        string applicationName,
        LoggingOptions options,
        FullNetLoggingMonitors monitors,
        Action<LoggerSinkConfiguration> configureGeneralSink,
        Action<LoggerSinkConfiguration> configureHighPrioritySink)
    {
        configuration
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", applicationName)
            .WriteTo.Logger(general => general
                .Filter.ByExcluding(
                    logEvent => logEvent.Level >= LogEventLevel.Error)
                .WriteTo.Async(
                    configureGeneralSink,
                    bufferSize: options.AsyncBufferSize,
                    blockWhenFull: false,
                    monitor: monitors.General))
            .WriteTo.Logger(highPriority => highPriority
                .MinimumLevel.Error()
                .WriteTo.Async(
                    configureHighPrioritySink,
                    bufferSize: options.HighPriorityAsyncBufferSize,
                    blockWhenFull: false,
                    monitor: monitors.HighPriority));

        return configuration;
    }
}
