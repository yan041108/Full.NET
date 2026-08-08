namespace Full.NET.Messaging.Kafka;

/// <summary>
/// Retry/DLQ 投递元数据头；与 Debezium Outbox 头分离，供运维与重放使用。
/// </summary>
public static class KafkaDeliveryHeaderNames
{
    public const string AttemptCount = "attempt_count";
    public const string FirstFailedAtUtc = "first_failed_at_utc";
    public const string LastFailedAtUtc = "last_failed_at_utc";
    public const string FailureCode = "failure_code";
    public const string FailureKind = "failure_kind";
    public const string FailureSummary = "failure_summary";
    public const string ConsumerName = "consumer_name";
    public const string SourceTopic = "source_topic";
    public const string SourcePartition = "source_partition";
    public const string SourceOffset = "source_offset";
}
