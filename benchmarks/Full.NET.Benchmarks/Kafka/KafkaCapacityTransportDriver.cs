using System.Buffers.Binary;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 定义未来 A/B/C 链路可共享的容量场景执行边界。
/// </summary>
public interface IKafkaCapacityScenarioDriver
{
    string ScopeCode { get; }

    Task<KafkaCapacitySampleEvidence> ExecuteAsync(
        KafkaCapacitySampleContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// 抽象真实 Kafka Producer/Consumer 传输执行器。
/// </summary>
public interface IKafkaCapacityTransportExecutor
{
    Task<KafkaCapacitySampleEvidence> ExecuteAsync(
        KafkaCapacitySampleContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// 保存单样本执行所需的预创建 Topic、临时 Group 和有界时间参数。
/// </summary>
public sealed class KafkaCapacitySampleContext
{
    private KafkaCapacitySampleContext(
        KafkaCapacitySample sample,
        KafkaCapacityTopicIdentity topicIdentity,
        string consumerGroupId,
        uint runHash,
        uint sampleHash,
        TimeSpan warmup,
        TimeSpan duration,
        TimeSpan drainTimeout,
        int maximumMessages)
    {
        Sample = sample;
        TopicIdentity = topicIdentity;
        ConsumerGroupId = consumerGroupId;
        RunHash = runHash;
        SampleHash = sampleHash;
        Warmup = warmup;
        Duration = duration;
        DrainTimeout = drainTimeout;
        MaximumMessages = maximumMessages;
    }

    public KafkaCapacitySample Sample { get; }

    public KafkaCapacityTopicIdentity TopicIdentity { get; }

    public string ConsumerGroupId { get; }

    public uint RunHash { get; }

    public uint SampleHash { get; }

    public TimeSpan Warmup { get; }

    public TimeSpan Duration { get; }

    public TimeSpan DrainTimeout { get; }

    public int MaximumMessages { get; }

    public static KafkaCapacitySampleContext Create(
        KafkaCapacitySample sample,
        KafkaCapacityTopicIdentity topicIdentity,
        string runId,
        TimeSpan? warmup = null,
        TimeSpan? duration = null,
        TimeSpan? drainTimeout = null,
        int maximumMessages = 1_000_000)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(topicIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        if (!string.Equals(
                sample.ScopeCode,
                KafkaCapacityScopeCodes.KafkaTransport,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Kafka transport context requires the kafka_transport scope.",
                nameof(sample));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumMessages);
        var resolvedWarmup = warmup ?? TimeSpan.Zero;
        var resolvedDuration = duration ?? TimeSpan.FromSeconds(30);
        var resolvedDrainTimeout = drainTimeout ?? TimeSpan.FromSeconds(60);
        if (resolvedWarmup < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(warmup));
        }

        if (resolvedDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        if (resolvedDrainTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(drainTimeout));
        }

        var runSegment = Normalize(runId);
        var sampleSegment = Normalize(sample.SampleId);
        return new KafkaCapacitySampleContext(
            sample,
            topicIdentity,
            $"fullnet.capacity.{runSegment}.{sampleSegment}.transport",
            Hash32(runId),
            Hash32(sample.SampleId),
            resolvedWarmup,
            resolvedDuration,
            resolvedDrainTimeout,
            maximumMessages);
    }

    private static uint Hash32(string value)
    {
        var bytes = Convert.FromHexString(KafkaCapacityFingerprint.Sha256(value));
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    private static string Normalize(string value)
    {
        var normalized = new string(value.ToLowerInvariant()
            .Select(static character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_'
                    ? character
                    : '-')
            .ToArray());
        return normalized.Length <= 80 ? normalized : normalized[..80];
    }
}

/// <summary>
/// 将独立 Kafka 传输执行器固定映射到 kafka_transport 证据范围。
/// </summary>
public sealed class KafkaTransportScenarioDriver : IKafkaCapacityScenarioDriver
{
    private readonly IKafkaCapacityTransportExecutor executor;

    public KafkaTransportScenarioDriver(IKafkaCapacityTransportExecutor executor)
    {
        this.executor = executor
            ?? throw new ArgumentNullException(nameof(executor));
    }

    public string ScopeCode => KafkaCapacityScopeCodes.KafkaTransport;

    public async Task<KafkaCapacitySampleEvidence> ExecuteAsync(
        KafkaCapacitySampleContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var evidence = await executor.ExecuteAsync(context, cancellationToken);
        if (!string.Equals(evidence.ScopeCode, ScopeCode, StringComparison.Ordinal)
            || !string.Equals(
                evidence.SampleId,
                context.Sample.SampleId,
                StringComparison.Ordinal)
            || evidence.Scenario != context.Sample.Scenario
            || evidence.TargetMessagesPerSecond
            != context.Sample.TargetMessagesPerSecond
            || evidence.PayloadSizeBytes != context.Sample.PayloadSizeBytes
            || evidence.ProducerConcurrency
            != context.Sample.ProducerConcurrency
            || evidence.Partitions != context.TopicIdentity.Partitions)
        {
            throw new InvalidDataException(
                "Kafka transport executor returned evidence for a different scope or sample.");
        }

        return evidence;
    }
}
