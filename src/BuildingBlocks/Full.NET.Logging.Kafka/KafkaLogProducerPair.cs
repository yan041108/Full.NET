using System.Diagnostics;
using System.Runtime.InteropServices;
using Full.NET.Hosting.Observability;

namespace Full.NET.Logging.Kafka;

/// <summary>将普通与优先 Kafka 日志放在独立 Producer，并共享一次停机排空预算。</summary>
public sealed class KafkaLogProducerPair : IDisposable
{
    private readonly KafkaLogDeliveryLane _general;
    private readonly KafkaLogDeliveryLane _priority;
    private readonly TimeSpan _shutdownTimeout;
    private int _disposed;

    internal KafkaLogProducerPair(
        KafkaLogDeliveryLane general,
        KafkaLogDeliveryLane priority,
        TimeSpan shutdownTimeout)
    {
        ArgumentNullException.ThrowIfNull(general);
        ArgumentNullException.ThrowIfNull(priority);
        if (ReferenceEquals(general, priority))
        {
            throw new ArgumentException("General and priority Producers must be independent.", nameof(priority));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(shutdownTimeout, TimeSpan.Zero);
        _general = general;
        _priority = priority;
        _shutdownTimeout = shutdownTimeout;
    }

    /// <summary>仅在显式构造适配时创建两个 Producer；第二个创建失败会清理第一个。</summary>
    /// <param name="options">经过验证的日志专用 Kafka 配置。</param>
    /// <returns>持有两个独立 Producer 的组合器。</returns>
    public static KafkaLogProducerPair Create(KafkaLogProducerOptions options) =>
        CreateWithFactory(options, KafkaLogDeliveryLane.Create);

    internal static KafkaLogProducerPair CreateWithFactory(
        KafkaLogProducerOptions options,
        Func<KafkaLogProducerOptions, bool, KafkaLogDeliveryLane> createLane)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(createLane);
        options.BuildProducerConfig();
        var general = createLane(options, false);
        try
        {
            var priority = createLane(options, true);
            return new KafkaLogProducerPair(
                general,
                priority,
                TimeSpan.FromMilliseconds(options.ShutdownFlushTimeoutMs));
        }
        catch
        {
            general.Stop(TimeSpan.Zero);
            throw;
        }
    }

    /// <summary>将已脱敏的 UTF-8 快照非阻塞提交到选定的独立 Producer。</summary>
    /// <param name="utf8Json">交出所有权后不得修改的紧凑 JSON 数组。</param>
    /// <param name="eventId">日志管道生成的稳定事件 ID。</param>
    /// <param name="highPriority">错误及更高级别使用优先通道。</param>
    /// <returns>仅表示本地提交状态，不表示 Broker 或最终存储确认。</returns>
    public KafkaLogProduceResult TryProduce(byte[] utf8Json, string eventId, bool highPriority)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return KafkaLogProduceResult.Stopped;
        }

        return (highPriority ? _priority : _general).TryProduce(utf8Json, eventId);
    }

    /// <summary>接收宿主已脱敏快照；完整数组直接传给后台 Producer，非数组内存才复制。</summary>
    /// <param name="snapshot">由宿主管道生成、事件 ID 与 JSON 一致的快照。</param>
    /// <returns>本地提交状态，Accepted 仍不等于 Broker 确认。</returns>
    public KafkaLogProduceResult TryProduce(HostLogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var memory = snapshot.Utf8Json;
        var payload = MemoryMarshal.TryGetArray(memory, out var segment)
                      && segment.Array is { } fullArray
                      && segment.Offset == 0
                      && segment.Count == fullArray.Length
            ? fullArray
            : memory.ToArray();
        return TryProduce(payload, snapshot.LogEventId, snapshot.IsHighPriority);
    }

    /// <summary>先关闭两个入口，再按同一个单调时钟截止时间依次排空优先和普通通道。</summary>
    /// <remarks>Flush 使用共享剩余预算；底层 native Dispose 的实际耗时仍须专项验证。</remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _priority.BeginStop();
        _general.BeginStop();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            _priority.Stop(Remaining(stopwatch.Elapsed));
        }
        finally
        {
            _general.Stop(Remaining(stopwatch.Elapsed));
        }
    }

    private TimeSpan Remaining(TimeSpan elapsed)
    {
        var remaining = _shutdownTimeout - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
