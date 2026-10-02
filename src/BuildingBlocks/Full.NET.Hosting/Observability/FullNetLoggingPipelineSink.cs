using System.Diagnostics;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Parsing;

namespace Full.NET.Hosting.Observability;

/// <summary>
/// 将普通与高优先级日志路由到独立队列，并在退出时共享同一排空预算。
/// </summary>
internal sealed class FullNetLoggingPipelineSink : ILogEventSink, IDisposable
{
    private static readonly MessageTemplate HttpTemplate =
        new MessageTemplateParser().Parse("HttpOperationCompleted");
    private readonly FullNetBoundedAsyncSink _general;
    private readonly FullNetBoundedAsyncSink _highPriority;
    private readonly TimeSpan _shutdownFlushTimeout;
    private readonly HttpOperationLogIngress? _httpOperationIngress;
    private readonly Func<HttpOperationLogRecord, LogEventLevel, bool> _emitHttpOperation;
    private readonly LoggingResourceMetadata? _resource;
    private readonly IDisposable? _externalExporter;
    private int _disposed;

    public FullNetLoggingPipelineSink(
        ILogEventSink generalSink,
        ILogEventSink highPrioritySink,
        LoggingOptions options,
        FullNetLoggingMonitors monitors,
        Action<LogEnvelope>? emitSnapshot = null,
        bool emitLegacySink = true,
        Action<HostLogSnapshot>? emitExternalSnapshot = null,
        LoggingResourceMetadata? resource = null,
        HttpOperationLogIngress? httpOperationIngress = null,
        IDisposable? externalExporter = null)
    {
        ArgumentNullException.ThrowIfNull(generalSink);
        ArgumentNullException.ThrowIfNull(highPrioritySink);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(monitors);

        _shutdownFlushTimeout = options.ShutdownFlushTimeout;
        _resource = resource;
        _externalExporter = externalExporter;
        _httpOperationIngress = httpOperationIngress;
        _emitHttpOperation = EmitHttpOperation;
        var routingPolicy = LogIndexRoutingPolicy.FromOptions(options);
        _general = new FullNetBoundedAsyncSink(
            generalSink,
            options.AsyncBufferSize,
            "Full.NET logging general",
            monitors.General,
            options.MaxEventBytes,
            options.GeneralQueueMaxBytes,
            emitSnapshot,
            emitLegacySink,
            emitExternalSnapshot,
            highPriority: false,
            routingPolicy: routingPolicy);
        _highPriority = new FullNetBoundedAsyncSink(
            highPrioritySink,
            options.HighPriorityAsyncBufferSize,
            "Full.NET logging high priority",
            monitors.HighPriority,
            options.MaxEventBytes,
            options.HighPriorityQueueMaxBytes,
            emitSnapshot,
            emitLegacySink,
            emitExternalSnapshot,
            highPriority: true,
            routingPolicy: routingPolicy);
        _httpOperationIngress?.Attach(_emitHttpOperation);
    }

    private bool EmitHttpOperation(HttpOperationLogRecord record, LogEventLevel level)
    {
        var properties = new List<LogEventProperty>
        {
            new("LogEventId", new ScalarValue(Guid.CreateVersion7().ToString("D"))),
        };
        if (_resource is not null)
        {
            properties.Add(new LogEventProperty("Instance", new ScalarValue(_resource.Instance)));
            properties.Add(new LogEventProperty("Application", new ScalarValue(_resource.Application)));
        }

        var source = new LogEvent(
            DateTimeOffset.UtcNow,
            level,
            null,
            HttpTemplate,
            properties);
        if (record.IsPriority)
        {
            return _highPriority.TryEmitTrustedHttp(source, record);
        }
        else
        {
            return _general.TryEmitTrustedHttp(source, record);
        }
    }

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        if (logEvent.Level >= LogEventLevel.Error)
        {
            _highPriority.Emit(logEvent);
            return;
        }

        _general.Emit(logEvent);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _httpOperationIngress?.Detach(_emitHttpOperation);

        var stopwatch = Stopwatch.StartNew();
        _general.Complete();
        _highPriority.Complete();

        var highPriorityCompleted = _highPriority.WaitForCompletion(
            GetRemainingTime(stopwatch.Elapsed));
        var generalCompleted = _general.WaitForCompletion(
            GetRemainingTime(stopwatch.Elapsed));

        _highPriority.AbandonPending();
        _general.AbandonPending();

        if (!highPriorityCompleted || !generalCompleted)
        {
            SelfLog.WriteLine(
                "Full.NET logging shutdown exceeded the shared flush timeout of {0}. "
                + "Pending in-memory events were abandoned.",
                _shutdownFlushTimeout);
        }

        // 外部 Producer 在双通道排空预算结束后关闭；超时未完成的快照按既有停机丢弃语义处理。
        _externalExporter?.Dispose();
    }

    private TimeSpan GetRemainingTime(TimeSpan elapsed)
    {
        var remaining = _shutdownFlushTimeout - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
