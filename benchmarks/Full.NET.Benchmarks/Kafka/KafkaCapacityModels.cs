namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 表示固定范围延迟直方图的不可变统计快照。
/// </summary>
public sealed record KafkaCapacityLatencySnapshot(
    long Count,
    long MinimumMicroseconds,
    long MaximumMicroseconds,
    long P50Microseconds,
    long P95Microseconds,
    long P99Microseconds,
    long OverflowCount)
{
    /// <summary>
    /// 获取是否没有发生破坏容量证据的范围溢出。
    /// </summary>
    public bool IsValid => OverflowCount == 0;
}

/// <summary>
/// 表示一次样本的消息传输正确性证据。
/// </summary>
public sealed record KafkaCapacityIntegrityEvidence(
    long Enqueued,
    long Acknowledged,
    long Consumed,
    long Lost,
    long Duplicate,
    long Corrupted,
    long OutOfOrder,
    long Unflushed,
    long InvalidSequence,
    bool DrainCompleted)
{
    /// <summary>
    /// 获取样本是否同时满足数量、完整性、顺序和排空硬门禁。
    /// </summary>
    public bool CorrectnessPassed =>
        Acknowledged == Consumed
        && Lost == 0
        && Duplicate == 0
        && Corrupted == 0
        && OutOfOrder == 0
        && Unflushed == 0
        && InvalidSequence == 0
        && DrainCompleted;
}

/// <summary>
/// 标识容量样本是否形成了完整关闭的证据。
/// </summary>
public enum KafkaCapacitySampleState
{
    Completed = 0,
    Incomplete = 1,
}

/// <summary>
/// 表示单个容量样本的性能测量白名单。
/// </summary>
public sealed record KafkaCapacityPerformanceEvidence(
    double ScheduledMessagesPerSecond,
    double AcknowledgedMessagesPerSecond,
    double ConsumedMessagesPerSecond,
    KafkaCapacityLatencySnapshot ScheduleLatency,
    KafkaCapacityLatencySnapshot AcknowledgementLatency,
    KafkaCapacityLatencySnapshot EndToEndLatency,
    long DrainMilliseconds,
    double CpuPercent,
    long ManagedHeapBytes,
    long LocalQueueMessages,
    long AllocatedBytes = 0,
    long WorkingSetBytes = 0,
    int Gen0Collections = 0,
    int Gen1Collections = 0,
    int Gen2Collections = 0,
    double ProducerEnqueuedMessagesPerSecond = 0,
    double DrainMessagesPerSecond = 0,
    long BrokerOffsetBacklogAtStop = 0,
    long OldestUnconsumedAgeUpperBoundMicroseconds = 0,
    long ManagedHeapPeakBytes = 0,
    long WorkingSetPeakBytes = 0,
    long BrokerOffsetBacklogAtDrainCompletion = 0,
    long OldestUnconsumedAgeUpperBoundAtDrainCompletionMicroseconds = 0,
    long LibrdkafkaStatisticsDroppedSnapshots = 0);

/// <summary>
/// Scope C 专用扩展证据；仅 transaction_outbox_cdc 样本写入报告。
/// </summary>
public sealed record KafkaCapacityOutboxCdcExtensionEvidence(
    long CdcPublished,
    KafkaCapacityLatencySnapshot OutboxCommitLatency,
    KafkaCapacityLatencySnapshot CdcToKafkaLatency);

/// <summary>
/// 表示可写入报告的单样本安全证据。
/// </summary>
public sealed record KafkaCapacitySampleEvidence(
    string ScopeCode,
    string SampleId,
    KafkaCapacityScenario Scenario,
    int TargetMessagesPerSecond,
    int PayloadSizeBytes,
    int Partitions,
    int ProducerConcurrency,
    KafkaCapacitySampleState State,
    KafkaCapacityIntegrityEvidence Integrity,
    KafkaCapacityPerformanceEvidence Performance,
    IReadOnlyList<string> FailureCodes,
    bool? PerformanceBudgetPassed = null,
    KafkaCapacityOutboxCdcExtensionEvidence? OutboxCdc = null);

/// <summary>
/// 表示 Kafka 集群的最小身份元数据。
/// </summary>
public sealed record KafkaCapacityClusterDescription(
    string ClusterId,
    int BrokerCount);

/// <summary>
/// 表示 Kafka Topic 的最小身份元数据。
/// </summary>
public sealed record KafkaCapacityTopicDescription(
    string TopicName,
    string TopicId,
    int Partitions,
    int ReplicationFactor);

/// <summary>
/// 表示经过集群摘要保护的临时 Topic 所有权证据。
/// </summary>
public sealed record KafkaCapacityTopicIdentity(
    string ClusterIdHash,
    string TopicName,
    string TopicId,
    int Partitions,
    int ReplicationFactor);

/// <summary>
/// 定义独立容量 Runner 的稳定进程退出码。
/// </summary>
public enum KafkaCapacityExitCode
{
    Success = 0,
    InvalidConfiguration = 2,
    EnvironmentRejected = 3,
    DependencyOrIncomplete = 4,
    CorrectnessFailed = 5,
    PerformanceBudgetFailed = 6,
    Cancelled = 130,
}

/// <summary>
/// 从样本硬门禁和可选预算结果解析稳定退出码。
/// </summary>
public static class KafkaCapacityExitCodeResolver
{
    public static KafkaCapacityExitCode Resolve(
        IReadOnlyList<KafkaCapacitySampleEvidence> samples,
        bool budgetProvided)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Any(static sample => !sample.Integrity.CorrectnessPassed))
        {
            return KafkaCapacityExitCode.CorrectnessFailed;
        }

        if (samples.Any(static sample =>
                sample.State == KafkaCapacitySampleState.Incomplete))
        {
            return KafkaCapacityExitCode.DependencyOrIncomplete;
        }

        if (budgetProvided
            && samples.Any(static sample => sample.PerformanceBudgetPassed != true))
        {
            return KafkaCapacityExitCode.PerformanceBudgetFailed;
        }

        return KafkaCapacityExitCode.Success;
    }
}
