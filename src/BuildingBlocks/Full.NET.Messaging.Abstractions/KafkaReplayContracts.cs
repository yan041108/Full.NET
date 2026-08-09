namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 一次性 Kafka 范围重放请求；时间范围与 Offset 范围必须二选一，且不修改正式 Consumer Group 水位。
/// </summary>
public sealed class KafkaReplayRequest
{
    public const int MaximumPartitions = 32;

    public KafkaReplayRequest(
        string topicCode,
        DateTimeOffset? fromTimestampUtc,
        DateTimeOffset? toTimestampUtc,
        long? fromOffset,
        long? toOffset,
        IReadOnlyList<int> partitions,
        string replayConsumerName,
        int maxMessages,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(topicCode)
            || !MessagingNames.TopicCodePattern.IsMatch(topicCode))
        {
            throw new ArgumentException(
                IntegrationEventFailureCodes.TopicCodeInvalid,
                nameof(topicCode));
        }

        ArgumentNullException.ThrowIfNull(partitions);
        if (partitions.Count > MaximumPartitions
            || partitions.Any(partition => partition < 0)
            || partitions.Distinct().Count() != partitions.Count)
        {
            throw new ArgumentException(
                $"Partitions must contain at most {MaximumPartitions} unique non-negative values.",
                nameof(partitions));
        }

        if (string.IsNullOrWhiteSpace(replayConsumerName)
            || replayConsumerName.Length > MessagingNames.ConsumerNameMaxLength
            || !MessagingNames.ConsumerNamePattern.IsMatch(replayConsumerName))
        {
            throw new ArgumentException(
                IntegrationEventFailureCodes.ConsumerNameInvalid,
                nameof(replayConsumerName));
        }

        if (maxMessages is < 1 or > 100_000)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMessages));
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 512)
        {
            throw new ArgumentException(
                "A replay audit reason between 1 and 512 characters is required.",
                nameof(reason));
        }

        var hasAnyTimestamp = fromTimestampUtc.HasValue || toTimestampUtc.HasValue;
        var hasCompleteTimestamp = fromTimestampUtc.HasValue && toTimestampUtc.HasValue;
        var hasAnyOffset = fromOffset.HasValue || toOffset.HasValue;
        var hasCompleteOffset = fromOffset.HasValue && toOffset.HasValue;
        if (hasAnyTimestamp != hasCompleteTimestamp
            || hasAnyOffset != hasCompleteOffset
            || hasCompleteTimestamp == hasCompleteOffset)
        {
            throw new ArgumentException(
                "Exactly one complete timestamp or offset range must be supplied.");
        }

        if (hasCompleteTimestamp
            && (fromTimestampUtc!.Value.Offset != TimeSpan.Zero
                || toTimestampUtc!.Value.Offset != TimeSpan.Zero
                || fromTimestampUtc > toTimestampUtc))
        {
            throw new ArgumentException(
                "Replay timestamps must be UTC and ordered from earliest to latest.");
        }

        if (hasCompleteOffset
            && (fromOffset < 0
                || toOffset < 0
                || toOffset == long.MaxValue
                || fromOffset > toOffset))
        {
            throw new ArgumentException(
                "Replay offsets must be non-negative and ordered from lowest to highest.");
        }

        TopicCode = topicCode;
        FromTimestampUtc = fromTimestampUtc;
        ToTimestampUtc = toTimestampUtc;
        FromOffset = fromOffset;
        ToOffset = toOffset;
        Partitions = partitions.ToArray();
        ReplayConsumerName = replayConsumerName;
        MaxMessages = maxMessages;
        Reason = reason.Trim();
    }

    public string TopicCode { get; }

    public DateTimeOffset? FromTimestampUtc { get; }

    public DateTimeOffset? ToTimestampUtc { get; }

    public long? FromOffset { get; }

    public long? ToOffset { get; }

    public IReadOnlyList<int> Partitions { get; }

    public string ReplayConsumerName { get; }

    public int MaxMessages { get; }

    public string Reason { get; }

    public bool UsesTimeRange => FromTimestampUtc.HasValue;

    public bool UsesOffsetRange => FromOffset.HasValue;
}

public interface IKafkaReplayService
{
    Task<KafkaReplayResult> ReplayAsync(
        KafkaReplayRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// API 一次性重放的运行门禁；默认关闭，避免把长时间 Broker 消费绑定到普通 HTTP 请求。
/// </summary>
public sealed record KafkaReplayExecutionPolicy(
    bool Enabled,
    int MaximumSynchronousMessages,
    TimeSpan ExecutionTimeout);

public sealed record KafkaReplayResult(
    int ScannedMessages,
    int ProcessedMessages,
    int AlreadyProcessedMessages,
    int RejectedMessages,
    bool LimitReached);
