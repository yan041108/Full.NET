using System.Collections.Concurrent;
using Confluent.Kafka;

namespace Full.NET.Logging.Kafka;

/// <summary>一次后台提交的本地结果；Accepted 仅表示 SDK 已接收，不代表 Broker 确认。</summary>
public enum KafkaLogProduceResult
{
    /// <summary>SDK 已接收，等待异步投递终态。</summary>
    Accepted,

    /// <summary>应用待确认预算已满。</summary>
    CapacityExceeded,

    /// <summary>记录超过配置的单消息上限。</summary>
    Oversize,

    /// <summary>SDK 同步拒绝提交。</summary>
    ProduceRejected,

    /// <summary>SDK 自身有界队列已满；与应用预算耗尽分别计量。</summary>
    SdkQueueFull,

    /// <summary>事件 ID 不符合受控的短 ASCII Kafka key 格式。</summary>
    InvalidEventId,

    /// <summary>当前通道已进入停机。</summary>
    Stopped,
}

/// <summary>一个 Topic 对应一个长生命周期 Producer 和独立的待确认预算。</summary>
/// <remarks>普通与优先通道必须各创建一个实例，才能隔离 SDK 的共享队列容量。</remarks>
public sealed class KafkaLogDeliveryLane : IDisposable
{
    private readonly object _submitGate = new();
    private readonly ConcurrentDictionary<long, KafkaLogProducerBudget.Reservation> _pending = new();
    private readonly IKafkaLogProducerClient _client;
    private readonly KafkaLogProducerBudget _budget;
    private readonly string _topic;
    private readonly int _maxMessageBytes;
    private readonly TimeSpan _flushTimeout;
    private long _nextId;
    private long _acknowledgedCount;
    private long _failedCount;
    private long _abandonedCount;
    private long _shutdownFailureCount;
    private int _stopped;
    private int _shutdownStarted;

    internal KafkaLogDeliveryLane(
        string topic,
        int maxPendingMessages,
        long maxPendingBytes,
        int maxMessageBytes,
        IKafkaLogProducerClient client,
        int shutdownFlushTimeoutMs = 5_000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMessageBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(shutdownFlushTimeoutMs);
        _topic = topic;
        _client = client;
        _budget = new KafkaLogProducerBudget(maxPendingMessages, maxPendingBytes);
        _maxMessageBytes = maxMessageBytes;
        _flushTimeout = TimeSpan.FromMilliseconds(shutdownFlushTimeoutMs);
    }

    /// <summary>按普通或优先路由构造独立的 Producer；调用时才创建 Kafka 客户端。</summary>
    /// <param name="options">已经提供 Broker、Topic 和容量的日志专用配置。</param>
    /// <param name="highPriority">为 true 时使用优先 Topic 和独立 SDK 队列。</param>
    /// <returns>由调用方负责在停机时释放的投递通道。</returns>
    public static KafkaLogDeliveryLane Create(KafkaLogProducerOptions options, bool highPriority)
    {
        ArgumentNullException.ThrowIfNull(options);
        var config = options.BuildProducerConfig();
        config.ClientId = $"{options.ClientId ?? "fullnet-log"}-{(highPriority ? "priority" : "general")}";
        var client = new ConfluentKafkaLogProducerClient(config);
        return new KafkaLogDeliveryLane(
            highPriority ? options.PriorityTopic! : options.GeneralTopic!,
            options.MaxPendingMessages,
            options.MaxPendingBytes,
            options.MessageMaxBytes,
            client,
            options.ShutdownFlushTimeoutMs);
    }

    /// <summary>当前尚未得到投递终态的应用侧消息数。</summary>
    public int ReservedMessages => _budget.ReservedMessages;

    /// <summary>当前尚未得到投递终态的估算字节数。</summary>
    public long ReservedBytes => _budget.ReservedBytes;

    /// <summary>Broker 投递报告为成功的累计数量。</summary>
    public long AcknowledgedCount => Interlocked.Read(ref _acknowledgedCount);

    /// <summary>同步提交或异步投递失败的累计数量。</summary>
    public long FailedCount => Interlocked.Read(ref _failedCount);

    /// <summary>停机预算结束后仍未获最终报告的累计数量。</summary>
    public long AbandonedCount => Interlocked.Read(ref _abandonedCount);

    /// <summary>SDK Flush 或 Dispose 在停机时失败的累计次数。</summary>
    public long ShutdownFailureCount => Interlocked.Read(ref _shutdownFailureCount);

    /// <summary>非阻塞提交后台快照，并将数组所有权转移给 SDK 直至投递终态。</summary>
    /// <param name="utf8Json">已脱敏的紧凑 JSON；调用后不得修改或复用此数组。</param>
    /// <param name="eventId">用于同一事件跨入口对账的稳定标识。</param>
    /// <returns>本地提交结果，Accepted 不表示 Broker 已确认。</returns>
    public KafkaLogProduceResult TryProduce(byte[] utf8Json, string eventId)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);
        // 日志管道生成 UUID v7；限定较短的 ASCII key，避免调用方绕过字节预算并让 SDK 编码巨量字符串。
        if (string.IsNullOrEmpty(eventId)
            || eventId.Length > 64
            || !eventId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-'))
        {
            return KafkaLogProduceResult.InvalidEventId;
        }

        // Kafka 消息尺寸还包含 key 与协议封套，不能仅检查 JSON 数组长度。
        if ((long)utf8Json.Length + eventId.Length + KafkaLogProducerBudget.EnvelopeOverheadBytes > _maxMessageBytes)
        {
            return KafkaLogProduceResult.Oversize;
        }

        lock (_submitGate)
        {
            if (_stopped != 0)
            {
                return KafkaLogProduceResult.Stopped;
            }

            if (!_budget.TryReserve(utf8Json.Length, eventId.Length, out var reservation))
            {
                return KafkaLogProduceResult.CapacityExceeded;
            }

            var id = Interlocked.Increment(ref _nextId);
            _pending[id] = reservation!;
            try
            {
                _client.Produce(_topic, eventId, utf8Json, acknowledged => Complete(id, acknowledged));
                return KafkaLogProduceResult.Accepted;
            }
            catch (KafkaException exception) when (exception.Error.Code == ErrorCode.Local_QueueFull)
            {
                Complete(id, acknowledged: false);
                return KafkaLogProduceResult.SdkQueueFull;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Produce 可在未入 SDK 队列时同步失败；与极端同步回调竞态仍只归还一次。
                Complete(id, acknowledged: false);
                return KafkaLogProduceResult.ProduceRejected;
            }
        }
    }

    /// <summary>单独使用时按本通道的 Flush 上限释放；双通道由组合器传入共享剩余预算。</summary>
    public void Dispose() => Stop(_flushTimeout);

    internal void BeginStop()
    {
        lock (_submitGate)
        {
            _stopped = 1;
        }
    }

    internal void Stop(TimeSpan flushTimeout)
    {
        BeginStop();
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
        {
            return;
        }

        if (flushTimeout < TimeSpan.Zero)
        {
            flushTimeout = TimeSpan.Zero;
        }

        try
        {
            _client.Flush(flushTimeout);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Flush 失败不延长停机预算；剩余消息按未确认统计。
            Interlocked.Increment(ref _shutdownFailureCount);
        }
        finally
        {
            try
            {
                try
                {
                    _client.Dispose();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // SDK 清理失败不阻止归还应用预约；错误只暴露为低基数计数。
                    Interlocked.Increment(ref _shutdownFailureCount);
                }
            }
            finally
            {
                foreach (var id in _pending.Keys)
                {
                    if (_pending.TryRemove(id, out var reservation))
                    {
                        reservation.Dispose();
                        Interlocked.Increment(ref _abandonedCount);
                    }
                }
            }
        }
    }

    private void Complete(long id, bool acknowledged)
    {
        if (!_pending.TryRemove(id, out var reservation))
        {
            return;
        }

        reservation.Dispose();
        if (acknowledged)
        {
            Interlocked.Increment(ref _acknowledgedCount);
        }
        else
        {
            Interlocked.Increment(ref _failedCount);
        }
    }
}

/// <summary>仅供适配器测试替换 SDK 的最小发送边界。</summary>
internal interface IKafkaLogProducerClient : IDisposable
{
    void Produce(string topic, string eventId, byte[] payload, Action<bool> onCompletion);
    int Flush(TimeSpan timeout);
}

internal sealed class ConfluentKafkaLogProducerClient(ProducerConfig config) : IKafkaLogProducerClient
{
    private readonly IProducer<string, byte[]> _producer = new ProducerBuilder<string, byte[]>(config).Build();

    public void Produce(string topic, string eventId, byte[] payload, Action<bool> onCompletion) =>
        _producer.Produce(
            topic,
            new Message<string, byte[]> { Key = eventId, Value = payload },
            report => onCompletion(!report.Error.IsError));

    public int Flush(TimeSpan timeout) => _producer.Flush(timeout);

    public void Dispose() => _producer.Dispose();
}
