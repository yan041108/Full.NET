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
        Action<LoggerSinkConfiguration>? configureHighPriorityWriteTo = null,
        LoggingResourceMetadata? resource = null,
        Action<LogEnvelope>? emitSnapshot = null,
        bool emitLegacySink = true,
        Action<HostLogSnapshot>? emitExternalSnapshot = null,
        HttpOperationLogIngress? httpOperationIngress = null,
        IDisposable? externalExporter = null)
    {
        resource ??= LoggingResourceMetadata.Create(applicationName, "Unknown");
        configuration
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            // 对解构输入先限长、限深和限集合，避免普通业务对象在入队前扩成无界属性图。
            .Destructure.ToMaximumDepth(4)
            .Destructure.ToMaximumCollectionCount(16)
            .Destructure.ToMaximumStringLength(2048)
            .Enrich.FromLogContext()
            .Enrich.With(new LogEventMetadataEnricher(resource.Instance))
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
                    monitors,
                    emitSnapshot,
                    emitLegacySink,
                    emitExternalSnapshot,
                    resource,
                    httpOperationIngress,
                    externalExporter));
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
