using System.Diagnostics.Metrics;
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

    /// <summary>
    /// 创建普通日志通道监控器。
    /// </summary>
    public FullNetAsyncLogMonitor() : this(GeneralChannel)
    {
    }

    internal FullNetAsyncLogMonitor(string channel)
    {
        _channel = channel;
        _meter.CreateObservableGauge<long>(
            "fullnet.logging.queue.depth",
            () => Observe(Snapshot.Count));
        _meter.CreateObservableGauge<long>(
            "fullnet.logging.queue.capacity",
            () => Observe(Snapshot.BufferSize));
        _meter.CreateObservableGauge<long>(
            "fullnet.logging.events.dropped",
            () => Observe(Snapshot.DroppedMessagesCount));
        _meter.CreateObservableGauge<long>(
            "fullnet.logging.queue.bytes",
            () => Observe((Volatile.Read(ref _inspector) as ILogByteBudgetInspector)?.ReservedBytes ?? 0));
        _meter.CreateObservableGauge<long>(
            "fullnet.logging.queue.bytes.capacity",
            () => Observe((Volatile.Read(ref _inspector) as ILogByteBudgetInspector)?.ByteCapacity ?? 0));
        _meter.CreateObservableGauge<long>(
            "fullnet.logging.events.oversize",
            () => Observe((Volatile.Read(ref _inspector) as ILogByteBudgetInspector)?.OversizeCount ?? 0));
        _meter.CreateObservableGauge<long>(
            "fullnet.logging.events.byte_budget_dropped",
            () => Observe((Volatile.Read(ref _inspector) as ILogByteBudgetInspector)?.ByteBudgetDroppedCount ?? 0));
    }

    /// <summary>
    /// 获取当前异步队列快照；Sink 尚未启动或已经停止时返回零值。
    /// </summary>
    public AsyncLogBufferSnapshot Snapshot
    {
        get
        {
            var inspector = Volatile.Read(ref _inspector);
            return inspector is null
                ? default
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
    public void StartMonitoring(IAsyncLogEventSinkInspector inspector) =>
        Volatile.Write(ref _inspector, inspector);

    /// <inheritdoc />
    public void StopMonitoring(IAsyncLogEventSinkInspector inspector) =>
        Interlocked.CompareExchange(ref _inspector, null, inspector);

    /// <inheritdoc />
    public void Dispose()
    {
        Volatile.Write(ref _inspector, null);
        _meter.Dispose();
    }

    private Measurement<long> Observe(long value) =>
        new(value, new KeyValuePair<string, object?>("channel", _channel));
}
