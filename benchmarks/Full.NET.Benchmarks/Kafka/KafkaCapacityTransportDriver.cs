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
        string producerClientId,
        string consumerClientId,
        uint runHash,
        uint sampleHash,
        TimeSpan warmup,
        TimeSpan duration,
        TimeSpan drainTimeout,
        int maximumMessages,
        long maximumScheduleLatencyMicroseconds)
    {
        Sample = sample;
        TopicIdentity = topicIdentity;
        ConsumerGroupId = consumerGroupId;
        ProducerClientId = producerClientId;
        ConsumerClientId = consumerClientId;
        RunHash = runHash;
        SampleHash = sampleHash;
        Warmup = warmup;
        Duration = duration;
        DrainTimeout = drainTimeout;
        MaximumMessages = maximumMessages;
        MaximumScheduleLatencyMicroseconds = maximumScheduleLatencyMicroseconds;
    }

    public KafkaCapacitySample Sample { get; }

    public KafkaCapacityTopicIdentity TopicIdentity { get; }

    public string ConsumerGroupId { get; }

    public string ProducerClientId { get; }

    public string ConsumerClientId { get; }

    public uint RunHash { get; }

    public uint SampleHash { get; }

    public TimeSpan Warmup { get; }

    public TimeSpan Duration { get; }

    public TimeSpan DrainTimeout { get; }

    public int MaximumMessages { get; }

    public long MaximumScheduleLatencyMicroseconds { get; }

    public static KafkaCapacitySampleContext Create(
        KafkaCapacitySample sample,
        KafkaCapacityTopicIdentity topicIdentity,
        string runId,
        TimeSpan? warmup = null,
        TimeSpan? duration = null,
        TimeSpan? drainTimeout = null,
        int maximumMessages = 1_000_000,
        long maximumScheduleLatencyMicroseconds = 5_000_000)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(topicIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        KafkaCapacityScopeCodes.Validate(sample.ScopeCode);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumMessages);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumScheduleLatencyMicroseconds);
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
        var baseIdentityPrefix =
            $"fullnet.capacity.{runSegment}.{sampleSegment}";
        var isTransportScope = string.Equals(
            sample.ScopeCode,
            KafkaCapacityScopeCodes.KafkaTransport,
            StringComparison.Ordinal);
        var scopeSegment = isTransportScope
            ? "transport"
            : Normalize(sample.ScopeCode);
        var scopeSuffix = $".{scopeSegment}";
        var clientScopeSuffix = isTransportScope ? string.Empty : scopeSuffix;
        return new KafkaCapacitySampleContext(
            sample,
            topicIdentity,
            BoundIdentity(baseIdentityPrefix, scopeSuffix),
            BoundIdentity(baseIdentityPrefix, $"{clientScopeSuffix}.producer"),
            BoundIdentity(baseIdentityPrefix, $"{clientScopeSuffix}.consumer"),
            Hash32(runId),
            Hash32(sample.SampleId),
            resolvedWarmup,
            resolvedDuration,
            resolvedDrainTimeout,
            maximumMessages,
            maximumScheduleLatencyMicroseconds);
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
        if (normalized.Length <= 80)
        {
            return normalized;
        }

        const int hashLength = 16;
        var hashSuffix = $"-{KafkaCapacityFingerprint.Sha256(normalized)[..hashLength]}";
        return normalized[..(80 - hashSuffix.Length)] + hashSuffix;
    }

    private static string BoundIdentity(string prefix, string suffix)
    {
        const int maximumLength = 200;
        if (suffix.Length >= maximumLength)
        {
            throw new ArgumentException(
                "Kafka capacity client identity suffix exceeds its hard bound.",
                nameof(suffix));
        }

        var boundedPrefix = prefix.Length + suffix.Length <= maximumLength
            ? prefix
            : BuildHashedPrefix(
                prefix,
                maximumLength - suffix.Length);
        return boundedPrefix + suffix;
    }

    private static string BuildHashedPrefix(string prefix, int maximumLength)
    {
        const int hashLength = 16;
        var hashSuffix = $"-{KafkaCapacityFingerprint.Sha256(prefix)[..hashLength]}";
        return prefix[..(maximumLength - hashSuffix.Length)] + hashSuffix;
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
