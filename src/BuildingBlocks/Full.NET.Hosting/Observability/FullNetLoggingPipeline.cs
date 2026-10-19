using Serilog;
using Serilog.Events;

namespace Full.NET.Hosting.Observability;

/// <summary>
/// 组装 Serilog 双通道有界管道；可选挂载本地文件、控制台快照或外部导出器，彼此独立消费同一 <see cref="LogEnvelope"/>。
/// </summary>
internal static class FullNetLoggingPipeline
{
    /// <summary>
    /// 配置普通与高优先级异步 Sink，并把可选出口接到统一的 <see cref="FullNetLoggingPipelineSink"/>。
    /// </summary>
    /// <param name="localFiles">启用 LocalFile 时由宿主创建的单例存储；生命周期与 Writer 绑定，禁止在请求线程直接写入。</param>
    public static LoggerConfiguration Configure(
        LoggerConfiguration configuration,
        string applicationName,
        LoggingOptions options,
        FullNetLoggingMonitors monitors,
        Action<LogEnvelope>? emitGeneralSnapshot,
        Action<LogEnvelope>? emitHighPrioritySnapshot,
        LoggingResourceMetadata? resource = null,
        Action<LogEnvelope>? emitSnapshot = null,
        Func<HostLogSnapshot, bool>? emitExternalSnapshot = null,
        HttpOperationLogIngress? httpOperationIngress = null,
        IHostLogSnapshotExporter? externalExporter = null,
        LocalFileLogStore? localFiles = null)
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

        configuration.WriteTo.Sink(new FullNetLoggingPipelineSink(
            emitGeneralSnapshot, emitHighPrioritySnapshot, options, monitors,
            emitSnapshot, emitExternalSnapshot, resource, httpOperationIngress,
            externalExporter, localFiles));
        return configuration;
    }
}
