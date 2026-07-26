using System.Diagnostics;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;

namespace Full.NET.Hosting.Observability;

/// <summary>
/// 将普通与高优先级日志路由到独立队列，并在退出时共享同一排空预算。
/// </summary>
internal sealed class FullNetLoggingPipelineSink : ILogEventSink, IDisposable
{
    private readonly FullNetBoundedAsyncSink _general;
    private readonly FullNetBoundedAsyncSink _highPriority;
    private readonly TimeSpan _shutdownFlushTimeout;
    private int _disposed;

    public FullNetLoggingPipelineSink(
        ILogEventSink generalSink,
        ILogEventSink highPrioritySink,
        LoggingOptions options,
        FullNetLoggingMonitors monitors)
    {
        ArgumentNullException.ThrowIfNull(generalSink);
        ArgumentNullException.ThrowIfNull(highPrioritySink);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(monitors);

        _shutdownFlushTimeout = options.ShutdownFlushTimeout;
        _general = new FullNetBoundedAsyncSink(
            generalSink,
            options.AsyncBufferSize,
            "Full.NET logging general",
            monitors.General);
        _highPriority = new FullNetBoundedAsyncSink(
            highPrioritySink,
            options.HighPriorityAsyncBufferSize,
            "Full.NET logging high priority",
            monitors.HighPriority);
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
    }

    private TimeSpan GetRemainingTime(TimeSpan elapsed)
    {
        var remaining = _shutdownFlushTimeout - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
