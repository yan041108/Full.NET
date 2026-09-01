using Full.NET.Abstractions.Results;
using Full.NET.Messaging.Abstractions;

namespace Full.NET.Modules.Messaging.Contracts;

/// <summary>
/// Messaging 运维操作的稳定权限码，不可本地化且作为服务端授权与客户端可见性的共同权威。
/// </summary>
public static class MessagingPermissions
{
    /// <summary>允许读取 Outbox 事件流目录与积压摘要。</summary>
    public const string EventsRead = "messaging.events.read";

    /// <summary>允许读取消费死信列表与详情。</summary>
    public const string DeadLettersRead = "messaging.dead_letters.read";

    /// <summary>允许重放单条消费死信，触发幂等消费。</summary>
    public const string DeadLettersReplay = "messaging.dead_letters.replay";

    /// <summary>允许按时间/偏移区间对 Kafka 事件流进行范围重放，属于高风险操作。</summary>
    public const string KafkaRangeReplay = "messaging.kafka.range_replay";

    /// <summary>允许将事件交付所有权从 Legacy 轮询切换到 CDC Kafka，属于高风险运维操作。</summary>
    public const string DeliveryCutover = "messaging.delivery.cutover";

    /// <summary>允许将事件交付所有权从 CDC Kafka 回退到 Legacy 轮询，属于高风险运维操作。</summary>
    public const string DeliveryRollback = "messaging.delivery.rollback";
}

/// <summary>
/// Messaging 模块稳定错误码集合，作为机器契约不可本地化。
/// </summary>
public static class MessagingErrorCodes
{
    /// <summary>
    /// 模块错误码的通用前缀；所有具体错误码均以前缀 + '.' + 后缀拼接，避免跨模块冲突。
    /// </summary>
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
    /// <summary>重放成功提交：消息重新进入消费者 Inbox 并被本次请求同步处理完毕。</summary>
    public const string Processed = "processed";

    /// <summary>已处理：重放请求在并发期间被其他操作提前消费，本次为幂等空转。</summary>
    public const string AlreadyProcessed = "already_processed";
}

/// <summary>消费死信响应契约，反映一条进入死信路径的消息及其最后一次失败信息。</summary>
/// <param name="ConsumerName">死信所属的消费者名，唯一标识一条消费链路。</param>
/// <param name="MessageId">原始集成事件的 MessageId。</param>
/// <param name="MessageType">事件类型的 CLR FullName 或等价稳定标识。</param>
/// <param name="SchemaVersion">事件载荷的 Schema 版本号。</param>
/// <param name="TenantId">事件所属租户标识；Host 级事件时为 null。</param>
/// <param name="Attempts">进入死信前已累计尝试次数。</param>
/// <param name="ReceivedAtUtc">消息首次被该消费者接收的时间（UTC）。</param>
/// <param name="LastErrorCode">最后一次失败的稳定错误码，可空。</param>
/// <param name="LastError">最后一次失败的可读错误消息或摘要，可空。</param>
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
/// <param name="ConsumerName">死信所属的消费者名。</param>
/// <param name="MessageId">原始集成事件的 MessageId。</param>
public sealed record ReplayDeadLetterRequest(string ConsumerName, Guid MessageId);

/// <summary>死信重放响应契约，<c>Outcome</c> 取 <see cref="DeadLetterReplayOutcomes"/> 的稳定值。</summary>
/// <param name="MessageId">被重放消息的 MessageId。</param>
/// <param name="ConsumerName">被重放消息所属的消费者名。</param>
/// <param name="Outcome">重放结果稳定机器码，取值自 DeadLetterReplayOutcomes。</param>
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
/// <param name="TopicCode">Kafka Topic 的稳定编码标识，用于目录映射到真实 Topic 名。</param>
/// <param name="FromTimestampUtc">扫描起始时间（UTC，含）；与 FromOffset 至少提供一个，同时提供时取先到达者。</param>
/// <param name="ToTimestampUtc">扫描结束时间（UTC，含）；与 ToOffset 至少提供一个，同时提供时取先到达者。</param>
/// <param name="FromOffset">扫描起始偏移量（含）；与 FromTimestampUtc 至少提供一个。</param>
/// <param name="ToOffset">扫描结束偏移量（含）；与 ToTimestampUtc 至少提供一个。</param>
/// <param name="Partitions">只扫描指定分区列表；空集合表示扫描该 Topic 的全部可用分区。</param>
/// <param name="ReplayConsumerName">此次重放使用的消费者名，决定消费端 Inbox 去重与业务 Handler 路由。</param>
/// <param name="MaxMessages">单次同步重放的最大消息数上限，用于限制单次运维操作影响面。</param>
/// <param name="Reason">运维操作理由，写入领域审计且不可编辑。</param>
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
/// <param name="ScannedMessages">扫描到的落入指定区间的 Kafka 消息总数。</param>
/// <param name="ProcessedMessages">实际提交给消费 Handler 并成功执行的消息数。</param>
/// <param name="AlreadyProcessedMessages">因幂等去重被判定已处理而跳过的消息数。</param>
/// <param name="RejectedMessages">重放请求校验阶段被直接拒绝的消息数（如未注册消费者或缺失 Schema）。</param>
/// <param name="LimitReached">true 表示扫描到 MaxMessages 上限后提前中止，区间未完全覆盖。</param>
public sealed record KafkaRangeReplayResponse(
    int ScannedMessages,
    int ProcessedMessages,
    int AlreadyProcessedMessages,
    int RejectedMessages,
    bool LimitReached);

/// <summary>Outbox 积压摘要响应契约，用于运维监控当前积压、到期重试、活动租约与死信规模。</summary>
/// <param name="PendingCount">尚未被任何发布者领取、等待发布的事件数。</param>
/// <param name="DueRetryCount">已达到下次重试时间但尚未被重新领取的事件数。</param>
/// <param name="ActiveLeaseCount">当前被发布者持有租约、正在发布或重试中的事件数。</param>
/// <param name="DeadLetterCount">发布侧死信（超过最大重试次数放弃）的总数。</param>
/// <param name="OldestOccurredAtUtc">积压中最早事件的发生时间，可空表示积压为空。</param>
/// <param name="OldestDeadLetteredAtUtc">最早发布侧死信的发生时间，可空表示无死信。</param>
public sealed record OutboxBacklogSummaryResponse(
    long PendingCount,
    long DueRetryCount,
    long ActiveLeaseCount,
    long DeadLetterCount,
    DateTimeOffset? OldestOccurredAtUtc,
    DateTimeOffset? OldestDeadLetteredAtUtc);

/// <summary>单条事件流交付状态响应契约，反映当前生效的交付所有者。</summary>
/// <param name="EventType">事件类型的稳定 CLR FullName 标识。</param>
/// <param name="SchemaVersion">该事件流的 Schema 版本号；不同版本视为独立流。</param>
/// <param name="TopicCode">Kafka Topic 的稳定编码标识，用于目录映射。</param>
/// <param name="DeliveryOwner">当前生效的交付所有者枚举值。</param>
public sealed record EventStreamStatusResponse(
    string EventType,
    int SchemaVersion,
    string TopicCode,
    EventDeliveryOwner DeliveryOwner);

/// <summary>交付状态总览响应契约，包含 Outbox 积压摘要与各事件流状态。</summary>
/// <param name="Backlog">发布侧 Outbox 队列积压快照。</param>
/// <param name="Streams">已在交付目录中登记的事件流状态列表，按 (EventType, SchemaVersion) 去重。</param>
public sealed record DeliveryStatusResponse(
    OutboxBacklogSummaryResponse Backlog,
    IReadOnlyList<EventStreamStatusResponse> Streams);

/// <summary>
/// 切换事件交付所有者的请求契约，目标所有者当前限定为 <see cref="EventDeliveryOwner.CdcKafka"/>。
/// </summary>
/// <remarks><c>Reason</c> 为必填，用于高风险运维审计追溯。</remarks>
/// <param name="EventType">目标事件流的事件类型稳定标识。</param>
/// <param name="SchemaVersion">目标事件流的 Schema 版本号。</param>
/// <param name="TargetOwner">切换后的目标交付所有者。</param>
/// <param name="Reason">运维变更理由，写入领域审计且不可编辑。</param>
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
/// <param name="EventType">被切换的事件流类型标识。</param>
/// <param name="SchemaVersion">被切换的事件流 Schema 版本号。</param>
/// <param name="CurrentOwner">切换前实际生效的交付所有者。</param>
/// <param name="TargetOwner">切换后持久化的交付所有者。</param>
/// <param name="OwnershipPersisted">所有权行是否已写入数据库；true 时后续解析立即生效。</param>
/// <param name="CutoffEventId">Legacy 链路保证已处理的最后一个事件标识；CDC Kafka 从此之后的事件开始接管。</param>
/// <param name="CutoffOccurredAtUtc">切换事务提交时间（UTC），用于审计。</param>
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
/// <param name="EventType">被回退的事件流类型标识。</param>
/// <param name="SchemaVersion">被回退的事件流 Schema 版本号。</param>
/// <param name="CurrentOwner">回退前实际生效的交付所有者。</param>
/// <param name="TargetOwner">回退后持久化的交付所有者。</param>
/// <param name="OwnershipPersisted">所有权行是否已写入数据库；true 时后续解析立即生效。</param>
/// <param name="RollbackBoundaryEventId">CDC Kafka 链路保证已发布并被 Legacy 接受的最后一个事件标识；回退后 Legacy 从该边界之后继续轮询。</param>
/// <param name="RollbackOccurredAtUtc">回退事务提交时间（UTC），用于审计。</param>
public sealed record DeliveryRollbackResponse(
    string EventType,
    int SchemaVersion,
    EventDeliveryOwner CurrentOwner,
    EventDeliveryOwner TargetOwner,
    bool OwnershipPersisted,
    Guid RollbackBoundaryEventId,
    DateTimeOffset RollbackOccurredAtUtc);
