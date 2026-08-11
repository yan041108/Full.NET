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
