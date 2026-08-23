using Full.NET.Abstractions.Results;
using Full.NET.Messaging.Abstractions;

namespace Full.NET.Modules.Messaging.Contracts;

/// <summary>
/// Messaging 运维操作的稳定权限码，不可本地化且作为服务端授权与客户端可见性的共同权威。
/// </summary>
public static class MessagingPermissions
{
    public const string EventsRead = "messaging.events.read";

    public const string DeadLettersRead = "messaging.dead_letters.read";

    public const string DeadLettersReplay = "messaging.dead_letters.replay";

    public const string KafkaRangeReplay = "messaging.kafka.range_replay";

    public const string DeliveryCutover = "messaging.delivery.cutover";

    public const string DeliveryRollback = "messaging.delivery.rollback";
}

/// <summary>
/// Messaging 模块稳定错误码集合，作为机器契约不可本地化。
/// </summary>
public static class MessagingErrorCodes
{
    public const string Prefix = "messaging.";

    /// <summary>消费死信未找到。</summary>
    public const string DeadLetterNotFound = "messaging.dead_letter.not_found";

    /// <summary>Outbox 事件未找到。</summary>
    public const string OutboxEventNotFound = "messaging.outbox_event.not_found";

    /// <summary>事件流的订阅路由未在目录中登记。</summary>
    public const string SubscriptionRouteNotFound = "messaging.subscription_route.not_found";

    /// <summary>Legacy Outbox 积压或死信未排空，不满足切流前置条件。</summary>
    public const string LegacyBacklogNotDrained = "messaging.delivery.legacy_backlog_not_drained";

    /// <summary>切流前置条件不满足（未启用、无持久化所有权行或未在目录登记）。</summary>
    public const string CutoverPreconditionFailed = "messaging.delivery.cutover_precondition_failed";

    /// <summary>切流目标所有者非法（当前仅允许切换到 CDC Kafka）。</summary>
    public const string InvalidCutoverTarget = "messaging.delivery.invalid_cutover_target";

    /// <summary>回退目标所有者非法（当前仅允许回退到 Legacy 轮询）。</summary>
    public const string InvalidRollbackTarget = "messaging.delivery.invalid_rollback_target";

    /// <summary>回退前置条件不满足。</summary>
    public const string RollbackPreconditionFailed = "messaging.delivery.rollback_precondition_failed";

    /// <summary>高风险运维操作必须提供理由。</summary>
    public const string ReasonRequired = "messaging.delivery.reason_required";

    /// <summary>切流 CAS 守卫失败：并发期间所有者已被其他操作变更。</summary>
    public const string CutoverConcurrencyConflict = "messaging.delivery.cutover_concurrency_conflict";

    /// <summary>Kafka 范围重放请求参数非法。</summary>
    public const string KafkaReplayRequestInvalid = "messaging.kafka.replay_request_invalid";

    /// <summary>Kafka 范围重放基础设施不可用。</summary>
    public const string KafkaReplayUnavailable = "messaging.kafka.replay_unavailable";

    /// <summary>同步重放超过配置的最大消息数限制，超出部分需异步重放。</summary>
    public const string KafkaReplaySynchronousLimitExceeded =
        "messaging.kafka.replay_synchronous_limit_exceeded";
}

/// <summary>
/// 死信重放结果机值，持久化与协议字段共享同一稳定字符串。
/// </summary>
public static class DeadLetterReplayOutcomes
{
    public const string Processed = "processed";

    public const string AlreadyProcessed = "already_processed";
}

/// <summary>消费死信响应契约，反映一条进入死信路径的消息及其最后一次失败信息。</summary>
public sealed record DeadLetterResponse(
    string ConsumerName,
    Guid MessageId,
    string MessageType,
    int SchemaVersion,
    Guid? TenantId,
    int Attempts,
    DateTimeOffset ReceivedAtUtc,
    string? LastErrorCode,
    string? LastError);

/// <summary>重放单条消费死信的请求契约，按消费者名与消息标识定位。</summary>
public sealed record ReplayDeadLetterRequest(string ConsumerName, Guid MessageId);

/// <summary>死信重放响应契约，<c>Outcome</c> 取 <see cref="DeadLetterReplayOutcomes"/> 的稳定值。</summary>
public sealed record DeadLetterReplayResponse(
    Guid MessageId,
    string ConsumerName,
    string Outcome);

/// <summary>
/// Kafka 范围重放请求契约，支持按时间区间或偏移量区间限定扫描范围。
/// </summary>
/// <remarks>
/// 重放必须指定 <c>Reason</c>；重放只触发既定消费业务的幂等副作用，消费端以
/// <c>(ConsumerName, MessageId)</c> Inbox 去重，重复重放不会产生重复业务写入。
/// </remarks>
public sealed record KafkaRangeReplayRequest(
    string TopicCode,
    DateTimeOffset? FromTimestampUtc,
    DateTimeOffset? ToTimestampUtc,
    long? FromOffset,
    long? ToOffset,
    IReadOnlyList<int> Partitions,
    string ReplayConsumerName,
    int MaxMessages,
    string Reason);

/// <summary>Kafka 范围重放响应契约，汇总扫描、处理、已处理与拒绝计数及是否触达上限。</summary>
public sealed record KafkaRangeReplayResponse(
    int ScannedMessages,
    int ProcessedMessages,
    int AlreadyProcessedMessages,
    int RejectedMessages,
    bool LimitReached);

/// <summary>Outbox 积压摘要响应契约，用于运维监控当前积压、到期重试、活动租约与死信规模。</summary>
public sealed record OutboxBacklogSummaryResponse(
    long PendingCount,
    long DueRetryCount,
    long ActiveLeaseCount,
    long DeadLetterCount,
    DateTimeOffset? OldestOccurredAtUtc,
    DateTimeOffset? OldestDeadLetteredAtUtc);

/// <summary>单条事件流交付状态响应契约，反映当前生效的交付所有者。</summary>
public sealed record EventStreamStatusResponse(
    string EventType,
    int SchemaVersion,
    string TopicCode,
    EventDeliveryOwner DeliveryOwner);

/// <summary>交付状态总览响应契约，包含 Outbox 积压摘要与各事件流状态。</summary>
public sealed record DeliveryStatusResponse(
    OutboxBacklogSummaryResponse Backlog,
    IReadOnlyList<EventStreamStatusResponse> Streams);

/// <summary>
/// 切换事件交付所有者的请求契约，目标所有者当前限定为 <see cref="EventDeliveryOwner.CdcKafka"/>。
/// </summary>
/// <remarks><c>Reason</c> 为必填，用于高风险运维审计追溯。</remarks>
public sealed record ChangeDeliveryOwnerRequest(
    string EventType,
    int SchemaVersion,
    EventDeliveryOwner TargetOwner,
    string Reason);

/// <summary>
/// 交付所有权切换响应契约，记录切换前后所有者与切流边界事件。
/// </summary>
/// <remarks>
/// <c>CutoffEventId</c> 标记 Legacy 发布链路的边界，切换后该流由目标所有者发布；
/// <c>OwnershipPersisted</c> 表示所有权行已持久化，后续 <see cref="EffectiveEventDeliveryOwnerResolver"/> 据此解析。
/// </remarks>
public sealed record DeliveryCutoverResponse(
    string EventType,
    int SchemaVersion,
    EventDeliveryOwner CurrentOwner,
    EventDeliveryOwner TargetOwner,
    bool OwnershipPersisted,
    Guid CutoffEventId,
    DateTimeOffset CutoffOccurredAtUtc);

/// <summary>
/// 交付所有权回退响应契约，记录回退前后所有者与回退边界事件。
/// </summary>
/// <remarks>
/// 回退将所有权从 CDC Kafka 退回 Legacy 轮询；<c>RollbackBoundaryEventId</c> 标记回退边界，
/// 回退同样经 CAS 守卫并在同一事务内写领域审计。
/// </remarks>
public sealed record DeliveryRollbackResponse(
    string EventType,
    int SchemaVersion,
    EventDeliveryOwner CurrentOwner,
    EventDeliveryOwner TargetOwner,
    bool OwnershipPersisted,
    Guid RollbackBoundaryEventId,
    DateTimeOffset RollbackOccurredAtUtc);
