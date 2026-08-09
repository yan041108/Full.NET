using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.Concurrent;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// Kafka Provider 低基数指标；禁止 MessageId、TenantId、原始 Topic 或异常文本标签。
/// </summary>
public static class KafkaMessagingTelemetry
{
    private const int MaximumConsumerStates = 1_024;
    public const string MeterName = "Full.NET.Messaging";
    public const string ActivitySourceName = "Full.NET.Messaging.Kafka";

    private static readonly Meter Meter = new(MeterName);
    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly ConcurrentDictionary<string, ConsumerState> ConsumerStates =
        new(StringComparer.Ordinal);
    private static long _compatibilityProcessingSequence;
    private static readonly Counter<long> ConsumeResults =
        Meter.CreateCounter<long>("fullnet.messaging.kafka.consume.results");
    private static readonly Counter<long> PartitionFlowResults =
        Meter.CreateCounter<long>("fullnet.messaging.kafka.partition.flow.results");
    private static readonly ObservableGauge<long> Inflight = Meter.CreateObservableGauge(
        "fullnet.messaging.kafka.inflight",
        () => Observe(state => state.Inflight));
    private static readonly ObservableGauge<long> BufferDepth = Meter.CreateObservableGauge(
        "fullnet.messaging.kafka.buffer.depth",
        () => Observe(state => state.BufferDepth));
    private static readonly ObservableGauge<long> AssignedPartitions = Meter.CreateObservableGauge(
        "fullnet.messaging.kafka.assigned.partitions",
        () => Observe(state => state.AssignedPartitions));
    private static readonly ObservableGauge<long> PausedPartitions = Meter.CreateObservableGauge(
        "fullnet.messaging.kafka.paused.partitions",
        () => Observe(state => state.PausedPartitions));
    private static readonly ObservableGauge<long> OwnershipRevoked = Meter.CreateObservableGauge(
        "fullnet.messaging.kafka.ownership.revoked",
        () => Observe(state => state.OwnershipRevoked ? 1 : 0));

    public static Activity? StartConsumeActivity(
        string topicCode,
        string consumerCode,
        int partition,
        long offset,
        string? traceParent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topicCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerCode);
        Activity? activity;
        if (!string.IsNullOrWhiteSpace(traceParent)
            && ActivityContext.TryParse(traceParent, null, true, out var parentContext))
        {
            activity = ActivitySource.StartActivity(
                "fullnet.messaging.kafka.consume",
                ActivityKind.Consumer,
                parentContext);
        }
        else
        {
            activity = ActivitySource.StartActivity(
                "fullnet.messaging.kafka.consume",
                ActivityKind.Consumer);
        }

        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", topicCode);
        activity?.SetTag("messaging.consumer.group.name", consumerCode);
        activity?.SetTag("messaging.kafka.partition", partition);
        activity?.SetTag("messaging.kafka.message.offset", offset);
        return activity;
    }

    public static Activity? StartCommitActivity(
        string consumerCode,
        int partitionCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerCode);
        if (partitionCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(partitionCount));
        }

        var activity = ActivitySource.StartActivity(
            "fullnet.messaging.kafka.commit",
            ActivityKind.Client);
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.consumer.group.name", consumerCode);
        activity?.SetTag("messaging.kafka.commit.partition_count", partitionCount);
        return activity;
    }

    public static void UpdateConsumerState(
        string provider,
        string consumerCode,
        int inflight,
        int bufferDepth,
        int assignedPartitions,
        int pausedPartitions,
        bool? ownershipRevoked = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerCode);
        if (inflight < 0
            || bufferDepth < 0
            || assignedPartitions < 0
            || pausedPartitions < 0
            || pausedPartitions > assignedPartitions)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferDepth));
        }

        try
        {
            if (!ConsumerStates.ContainsKey(consumerCode)
                && ConsumerStates.Count >= MaximumConsumerStates)
            {
                return;
            }

            ConsumerStates.AddOrUpdate(
                consumerCode,
                _ => new ConsumerState(
                    provider,
                    consumerCode,
                    inflight,
                    bufferDepth,
                    assignedPartitions,
                    pausedPartitions,
                    ownershipRevoked ?? false,
                    ProcessingSequence: 0),
                (_, current) => current with
                {
                    Provider = provider,
                    AssignedPartitions = assignedPartitions,
                    PausedPartitions = pausedPartitions,
                    OwnershipRevoked = ownershipRevoked ?? current.OwnershipRevoked,
                });
        }
        catch (Exception)
        {
            // 状态遥测旁路失败不得影响消费、提交或 Rebalance。
        }
    }

    public static void SetOwnershipRevoked(string consumerCode, bool value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerCode);
        try
        {
            while (ConsumerStates.TryGetValue(consumerCode, out var current))
            {
                if (ConsumerStates.TryUpdate(
                        consumerCode,
                        current with { OwnershipRevoked = value },
                        current))
                {
                    return;
                }
            }
        }
        catch (Exception)
        {
            // 所有权遥测失败不得改变 Fence 行为。
        }
    }

    public static void UpdateProcessingState(
        string provider,
        string consumerCode,
        int inflight,
        int bufferDepth) =>
        UpdateProcessingState(
            provider,
            consumerCode,
            Interlocked.Increment(ref _compatibilityProcessingSequence),
            inflight,
            bufferDepth);

    internal static void UpdateProcessingState(
        string provider,
        string consumerCode,
        long sequence,
        int inflight,
        int bufferDepth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerCode);
        if (sequence < 1 || inflight < 0 || bufferDepth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferDepth));
        }

        try
        {
            while (ConsumerStates.TryGetValue(consumerCode, out var current))
            {
                if (sequence <= current.ProcessingSequence)
                {
                    return;
                }

                if (ConsumerStates.TryUpdate(
                        consumerCode,
                        current with
                        {
                            Provider = provider,
                            Inflight = inflight,
                            BufferDepth = bufferDepth,
                            ProcessingSequence = sequence,
                        },
                        current))
                {
                    return;
                }
            }
        }
        catch (Exception)
        {
            // Handler 热路径状态采集失败不得影响消息处理。
        }
    }

    public static void RemoveConsumerState(string consumerCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerCode);
        try
        {
            ConsumerStates.TryRemove(consumerCode, out _);
        }
        catch (Exception)
        {
            // 清理遥测状态失败不得阻塞 Worker 退出。
        }
    }

    public static void RecordConsume(
        string provider,
        string topicCode,
        string consumerCode,
        string messageTypeCode,
        string result,
        string? reasonCode = null)
    {
        Record(
            ConsumeResults,
            provider,
            topicCode,
            consumerCode,
            messageTypeCode,
            result,
            reasonCode);
    }

    public static void RecordPartitionFlow(
        string provider,
        string topicCode,
        string consumerCode,
        string result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(topicCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(result);

        try
        {
            PartitionFlowResults.Add(
                1,
                new TagList
                {
                    { "provider", provider },
                    { "topic_code", topicCode },
                    { "consumer_code", consumerCode },
                    { "result", result },
                });
        }
        catch (Exception)
        {
            // 指标旁路失败不得影响分区背压与 Offset 语义。
        }
    }

    private static void Record(
        Counter<long> counter,
        string provider,
        string topicCode,
        string consumerCode,
        string messageTypeCode,
        string result,
        string? reasonCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(topicCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageTypeCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(result);

        try
        {
            var tags = new TagList
            {
                { "provider", provider },
                { "topic_code", topicCode },
                { "consumer_code", consumerCode },
                { "message_type_code", messageTypeCode },
                { "result", result },
            };

            if (!string.IsNullOrWhiteSpace(reasonCode))
            {
                tags.Add("reason_code", reasonCode);
            }

            counter.Add(1, tags);
        }
        catch (Exception)
        {
            // 指标旁路失败不得影响消费语义。
        }
    }

    private static IEnumerable<Measurement<long>> Observe(
        Func<ConsumerState, long> valueSelector)
    {
        foreach (var state in ConsumerStates.Values)
        {
            yield return new Measurement<long>(
                valueSelector(state),
                new KeyValuePair<string, object?>("provider", state.Provider),
                new KeyValuePair<string, object?>("consumer_code", state.ConsumerCode));
        }
    }

    private sealed record ConsumerState(
        string Provider,
        string ConsumerCode,
        int Inflight,
        int BufferDepth,
        int AssignedPartitions,
        int PausedPartitions,
        bool OwnershipRevoked,
        long ProcessingSequence);
}
