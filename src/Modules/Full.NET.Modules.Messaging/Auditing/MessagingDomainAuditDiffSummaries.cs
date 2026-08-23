namespace Full.NET.Modules.Messaging.Auditing;

/// <summary>交付权切换审计摘要；供源生成 JSON 序列化，避免匿名类型反射路径。</summary>
internal sealed record DeliveryCutoverAuditDiff(
    string EventType,
    int SchemaVersion,
    string CurrentOwner,
    string TargetOwner,
    Guid CutoffEventId,
    DateTimeOffset CutoffOccurredAtUtc,
    string? Reason,
    bool OwnershipPersisted);

/// <summary>交付权回滚审计摘要。</summary>
internal sealed record DeliveryRollbackAuditDiff(
    string EventType,
    int SchemaVersion,
    string CurrentOwner,
    string TargetOwner,
    Guid RollbackGeneration,
    string? ProducerFencePosition,
    Guid RollbackBoundaryEventId,
    DateTimeOffset RollbackOccurredAtUtc,
    string? Reason,
    bool OwnershipPersisted);

/// <summary>死信重放审计摘要。</summary>
internal sealed record DeadLetterReplayAuditDiff(
    string ConsumerName,
    Guid MessageId,
    string Outcome);

/// <summary>Kafka 区间重放请求审计摘要。</summary>
internal sealed record KafkaRangeReplayRequestedAuditDiff(
    string TopicCode,
    IReadOnlyList<int>? Partitions,
    DateTimeOffset? FromTimestampUtc,
    DateTimeOffset? ToTimestampUtc,
    long? FromOffset,
    long? ToOffset,
    string ReplayConsumerName,
    int MaxMessages,
    string? Reason);

/// <summary>Kafka 区间重放取消/超时审计摘要。</summary>
internal sealed record KafkaRangeReplayCancelledAuditDiff(
    string TopicCode,
    string ReplayConsumerName,
    string ReasonCode);

/// <summary>Kafka 区间重放失败审计摘要。</summary>
internal sealed record KafkaRangeReplayFailedAuditDiff(
    string TopicCode,
    string ReplayConsumerName,
    string ReasonCode,
    string ExceptionType);

/// <summary>Kafka 区间重放成功审计摘要。</summary>
internal sealed record KafkaRangeReplaySuccessAuditDiff(
    string TopicCode,
    string ReplayConsumerName,
    int ScannedMessages,
    int ProcessedMessages,
    int AlreadyProcessedMessages,
    int RejectedMessages,
    bool LimitReached);
