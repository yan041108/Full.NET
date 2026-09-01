namespace Full.NET.Messaging.Kafka;

/// <summary>
/// Retry/DLQ 投递元数据头；与 Debezium Outbox 头分离，供运维与重放使用。
/// </summary>
public static class KafkaDeliveryHeaderNames
{
    /// <summary>当前累计重试次数（从 1 开始）。</summary>
    public const string AttemptCount = "attempt_count";
    /// <summary>首次失败时间（UTC，ISO 8601）。</summary>
    public const string FirstFailedAtUtc = "first_failed_at_utc";
    /// <summary>最近一次失败时间（UTC，ISO 8601）。</summary>
    public const string LastFailedAtUtc = "last_failed_at_utc";
    /// <summary>本消息最早可重试时间（UTC，ISO 8601）；早于此时刻应跳过重投。</summary>
    public const string RetryNotBeforeUtc = "retry_not_before_utc";
    /// <summary>最后一次失败的稳定错误码；为空表示分类器未能识别。</summary>
    public const string FailureCode = "failure_code";
    /// <summary>失败类别：transient / permanent / unknown。</summary>
    public const string FailureKind = "failure_kind";
    /// <summary>最后一次失败的可读摘要；仅用于运维排查，不作为机器契约。</summary>
    public const string FailureSummary = "failure_summary";
    /// <summary>产生本头的消费者名称，用于跨消费者死信隔离。</summary>
    public const string ConsumerName = "consumer_name";
    /// <summary>失败消费来源 Topic。</summary>
    public const string SourceTopic = "source_topic";
    /// <summary>失败消费来源 Partition（十进制字符串）。</summary>
    public const string SourcePartition = "source_partition";
    /// <summary>失败消费来源 Offset（十进制字符串）。</summary>
    public const string SourceOffset = "source_offset";
}
