using System.Diagnostics.Metrics;
using System.Diagnostics;
using Serilog.Events;
using Serilog.Sinks.Async;

namespace Full.NET.Hosting.Observability;

/// <summary>
/// 表示异步日志队列的当前容量与累计丢弃快照。
/// </summary>
public readonly record struct AsyncLogBufferSnapshot(
    int Count,
    int BufferSize,
    long DroppedMessagesCount);

/// <summary>
/// 供日志监控器读取字节预算和过大事件计数的内部视图。
/// </summary>
internal interface ILogByteBudgetInspector
{
    long ReservedBytes { get; }
    long ByteCapacity { get; }
    long OversizeCount { get; }
    long ByteBudgetDroppedCount { get; }
}

/// <summary>消费进展视图；年龄包括下游仍未返回的当前记录，不以出队冒充投递完成。</summary>
internal interface ILogProgressInspector
{
    double OldestPendingSeconds { get; }
}

/// <summary>
/// 观测单条固定日志通道的队列状态，并暴露低基数 OpenTelemetry 指标。
/// </summary>
public sealed class FullNetAsyncLogMonitor : IAsyncLogEventSinkMonitor, IDisposable
{
    /// <summary>
    /// OpenTelemetry 注册与监听共同使用的稳定 Meter 名称。
    /// </summary>
    public const string MeterName = "Full.NET.Logging";

    internal const string GeneralChannel = "general";
    internal const string HighPriorityChannel = "high_priority";

    private readonly string _channel;
    private readonly Meter _meter = new(MeterName);
    private IAsyncLogEventSinkInspector? _inspector;
    private long _terminalDropped;
    private long _terminalOversize;
    private long _terminalByteDropped;
    private long _lastProgress;
    private int _workerState;
    private readonly Counter<long> _offered;
    private readonly Counter<long> _consumed;

    // 0 未装配、1 运行、2 停止接收、3 已停止、4 故障；这些值属于监控契约。
    internal int WorkerState => Volatile.Read(ref _workerState);
    internal double OldestPendingSeconds => (Volatile.Read(ref _inspector) as ILogProgressInspector)?.OldestPendingSeconds ?? 0;

    /// <summary>
    /// 创建普通日志通道监控器。
    /// </summary>
    public FullNetAsyncLogMonitor() : this(GeneralChannel)
    {
    }

    internal FullNetAsyncLogMonitor(string channel)
    {
        _channel = channel;
        _offered = _meter.CreateCounter<long>("fullnet.logging.events.offered");
        _consumed = _meter.CreateCounter<long>("fullnet.logging.events.consumed");
        _meter.CreateObservableGauge<long>(
            "fullnet.logging.queue.depth",
            () => Observe(Snapshot.Count));
        _meter.CreateObservableGauge<long>(
            "fullnet.logging.queue.capacity",
            () => Observe(Snapshot.BufferSize));
        _meter.CreateObservableCounter<long>(
            "fullnet.logging.events.dropped",
            () => Observe(Snapshot.DroppedMessagesCount));
        _meter.CreateObservableGauge<long>(
            "fullnet.logging.queue.bytes",
            () => Observe((Volatile.Read(ref _inspector) as ILogByteBudgetInspector)?.ReservedBytes ?? 0));
        _meter.CreateObservableGauge<long>(
            "fullnet.logging.queue.bytes.capacity",
            () => Observe((Volatile.Read(ref _inspector) as ILogByteBudgetInspector)?.ByteCapacity ?? 0));
        _meter.CreateObservableCounter<long>(
            "fullnet.logging.events.oversize",
            () => Observe((Volatile.Read(ref _inspector) as ILogByteBudgetInspector)?.OversizeCount ?? Interlocked.Read(ref _terminalOversize)));
        _meter.CreateObservableCounter<long>(
            "fullnet.logging.events.byte_budget_dropped",
            () => Observe((Volatile.Read(ref _inspector) as ILogByteBudgetInspector)?.ByteBudgetDroppedCount ?? Interlocked.Read(ref _terminalByteDropped)));
        _meter.CreateObservableGauge<long>("fullnet.logging.worker.state", () => Observe(WorkerState));
        _meter.CreateObservableGauge<double>("fullnet.logging.queue.oldest_age", () =>
            new Measurement<double>(OldestPendingSeconds, new KeyValuePair<string, object?>("channel", _channel)), unit: "s");
        _meter.CreateObservableGauge<double>("fullnet.logging.worker.progress_age", () =>
            new Measurement<double>(ProgressAgeSeconds,
                new KeyValuePair<string, object?>("channel", _channel)), unit: "s");
    }

    /// <summary>
    /// 获取当前异步队列快照；停止后保留累计丢弃，不能把停机损失显示成零。
    /// </summary>
    public AsyncLogBufferSnapshot Snapshot
    {
        get
        {
            var inspector = Volatile.Read(ref _inspector);
            return inspector is null
                ? new AsyncLogBufferSnapshot(0, 0, Interlocked.Read(ref _terminalDropped))
                : new AsyncLogBufferSnapshot(
                    inspector.Count,
                    inspector.BufferSize,
                    inspector.DroppedMessagesCount);
        }
    }

    internal (long ReservedBytes, long CapacityBytes) ByteUsage
    {
        get
        {
            var inspector = Volatile.Read(ref _inspector) as ILogByteBudgetInspector;
            return inspector is null
                ? default
                : (inspector.ReservedBytes, inspector.ByteCapacity);
        }
    }

    /// <inheritdoc />
    public void StartMonitoring(IAsyncLogEventSinkInspector inspector)
    {
        Volatile.Write(ref _inspector, inspector);
        Interlocked.Exchange(ref _lastProgress, Stopwatch.GetTimestamp());
        Volatile.Write(ref _workerState, 1);
    }

    /// <inheritdoc />
    public void StopMonitoring(IAsyncLogEventSinkInspector inspector)
    {
        if (!ReferenceEquals(Volatile.Read(ref _inspector), inspector)) return;
        Interlocked.Exchange(ref _terminalDropped, inspector.DroppedMessagesCount);
        if (inspector is ILogByteBudgetInspector bytes)
        {
            Interlocked.Exchange(ref _terminalOversize, bytes.OversizeCount);
            Interlocked.Exchange(ref _terminalByteDropped, bytes.ByteBudgetDroppedCount);
        }
        Interlocked.CompareExchange(ref _inspector, null, inspector);
        if (WorkerState != 4) Volatile.Write(ref _workerState, 3);
    }

    internal void WorkerStopping() => Interlocked.CompareExchange(ref _workerState, 2, 1);
    internal void WorkerFailed() => Volatile.Write(ref _workerState, 4);
    private double ProgressAgeSeconds => Interlocked.Read(ref _lastProgress) is var stamp && stamp != 0
        ? Stopwatch.GetElapsedTime(stamp).TotalSeconds : 0;
    internal void RecordOffered(LogEventLevel level)
    {
        try { _offered.Add(1, new KeyValuePair<string, object?>("channel", _channel),
            new KeyValuePair<string, object?>("level", level == LogEventLevel.Fatal ? "Critical" : level.ToString())); }
        catch (Exception) { /* 指标旁路不能影响生产者。 */ }
    }
    internal void RecordConsumed(bool failed)
    {
        Interlocked.Exchange(ref _lastProgress, Stopwatch.GetTimestamp());
        try { _consumed.Add(1, new KeyValuePair<string, object?>("channel", _channel),
            new KeyValuePair<string, object?>("outcome", failed ? "failed" : "accepted")); }
        catch (Exception) { /* 指标旁路不能停止消费者。 */ }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Volatile.Write(ref _inspector, null);
        _meter.Dispose();
    }

    private Measurement<long> Observe(long value) =>
        new(value, new KeyValuePair<string, object?>("channel", _channel));
}
