namespace Full.NET.Modules.Webhooks.Contracts;

/// <summary>
/// Webhook 模块稳定权限码集合；不可本地化，作为服务端授权与客户端可见性的共同权威。
/// </summary>
/// <remarks>权限码字符串发布后不可改名或删除；新增权限只能追加到本类末尾，避免破坏既有角色分配与策略缓存。</remarks>
public static class WebhookPermissions
{
    /// <summary>允许读取 Webhook 订阅列表与详情，以及最近投递记录。</summary>
    public const string SubscriptionsRead = "webhooks.subscriptions.read";

    /// <summary>允许创建 Webhook 订阅；状态变更仍要求独立权限。</summary>
    public const string SubscriptionsManage = "webhooks.subscriptions.manage";

    /// <summary>允许按当前版本暂停或恢复本企业 Webhook 订阅。</summary>
    public const string SubscriptionsSetStatus = "webhooks.subscriptions.set_status";

    /// <summary>允许读取本企业指定订阅的投递历史、当前计数及安全失败类别。</summary>
    public const string DeliveriesRead = "webhooks.deliveries.read";

    /// <summary>允许按原版本手动排队本企业的终态失败投递。</summary>
    public const string DeliveriesReplay = "webhooks.deliveries.replay";

    /// <summary>确认接收方已提交的恢复投递。</summary>
    public const string DeliveriesReconcile = "webhooks.deliveries.reconcile";

    /// <summary>允许准备、明确激活或取消本企业订阅的签名密钥轮换。</summary>
    public const string SigningKeysRotate = "webhooks.subscriptions.rotate_signing_key";
}

/// <summary>
/// Webhook 投递状态稳定机器码；持久化与协议字段共享同一字符串，发布后不可重命名或重排。
/// </summary>
public static class WebhookDeliveryStatuses
{
    /// <summary>待投递：事件已入队但尚未被发布者领取，或租约尚未到期可重试。</summary>
    public const string Pending = "Pending";

    /// <summary>已交付：目标端点返回成功响应，视为本次投递完成。</summary>
    public const string Delivered = "Delivered";

    /// <summary>失败：超过最大重试次数仍未成功，进入毒消息路径不再自动重投。</summary>
    public const string Failed = "Failed";
}

/// <summary>
/// Webhook 模块稳定错误码集合；前缀为机器契约不可本地化，新增错误码只能追加。
/// </summary>
public static class WebhookErrorCodes
{
    /// <summary>同一租户事件流的订阅扇出超过工作预算。</summary>
    public const string FanoutBudgetExceeded = "webhooks.fanout_budget_exceeded";

    /// <summary>模块错误码通用前缀；所有具体错误码以前缀 + '.' + 后缀拼接，避免跨模块冲突。</summary>
    public const string Prefix = "webhooks.";

    /// <summary>指定订阅未找到；订阅标识不存在或已被删除。</summary>
    public const string SubscriptionNotFound = "webhooks.subscription.not_found";

    /// <summary>订阅版本已变化，必须读取当前状态并重新确认。</summary>
    public const string SubscriptionVersionConflict = "webhooks.subscription.version_conflict";

    /// <summary>事件键、目标或签名材料缺失，或超出输入预算。</summary>
    public const string SubscriptionInvalid = "webhooks.subscription.invalid";

    /// <summary>当前企业的指定订阅下未找到该投递记录。</summary>
    public const string DeliveryNotFound = "webhooks.delivery.not_found";

    /// <summary>重放操作键、版本、订阅启用条件或原投递状态冲突。</summary>
    public const string ReplayConflict = "webhooks.delivery.replay_conflict";

    /// <summary>恢复操作身份、投递状态或证据冲突。</summary>
    public const string RecoveryConflict = "webhooks.delivery.recovery_conflict";

    /// <summary>当前操作者在原资源下没有对应重放回执。</summary>
    public const string ReplayReceiptNotFound = "webhooks.delivery.replay_receipt_not_found";

    /// <summary>轮换操作、原版本、待准备身份或完成状态冲突。</summary>
    public const string SigningKeyRotationConflict = "webhooks.signing_key_rotation.conflict";

    /// <summary>原订阅没有对应的轮换记录或待激活身份。</summary>
    public const string SigningKeyRotationNotFound = "webhooks.signing_key_rotation.not_found";

    /// <summary>订阅目标 URL 非法：未通过 HTTPS、主机名解析或回调地址校验。</summary>
    public const string TargetUrlInvalid = "webhooks.subscription.target_url_invalid";
}

/// <summary>
/// Webhook 订阅响应契约；字段顺序为稳定机器码的一部分，新增可选字段只能追加。
/// </summary>
/// <remarks>
/// SigningSecret 不在响应中回显，避免向客户端泄漏签名密钥。
/// </remarks>
/// <param name="Id">订阅稳定标识。</param>
/// <param name="TenantId">所属租户标识；Host 级订阅为 Guid.Empty。</param>
/// <param name="EventType">订阅的事件类型稳定标识（CLR FullName 或目录登记键）。</param>
/// <param name="TargetUrl">接收投递的 HTTPS 端点绝对地址。</param>
/// <param name="IsActive">订阅是否处于启用状态；停用后不再产生新的投递。</param>
/// <param name="Version">乐观并发版本号，用于更新、删除与启用/停用请求的 CAS 守卫。</param>
/// <param name="SigningKeyId">当前签名密钥的公开标识，不包含签名材料。</param>
public sealed record WebhookSubscriptionResponse(
    Guid Id,
    Guid TenantId,
    string EventType,
    string TargetUrl,
    bool IsActive,
    int Version,
    Guid SigningKeyId);

/// <summary>
/// 创建 Webhook 订阅的请求契约；SigningSecret 由服务端加密存储，响应永不回显明文。
/// </summary>
/// <remarks>
/// 当前创建不接受幂等键，未知结果须先核对列表，客户端不得自动重放。
/// </remarks>
/// <param name="EventType">订阅的事件类型稳定标识。</param>
/// <param name="TargetUrl">接收投递的 HTTPS 端点绝对地址；必须通过 URL 合法性校验。</param>
/// <param name="SigningSecret">用于签名投递负载的 16～128 字符密钥；以 Data Protection 加密持久化。</param>
public sealed record CreateWebhookSubscriptionRequest(
    string EventType,
    string TargetUrl,
    string SigningSecret);

/// <summary>按明确修订暂停或恢复本企业订阅，不修改事件、目标和签名材料。</summary>
/// <param name="IsActive">目标启用状态；停用阻止后续扇出与领取，不撤回已发出的 HTTP 请求。</param>
/// <param name="Version">用户确认时读取的正整数修订。</param>
public sealed record SetWebhookSubscriptionStatusRequest(bool IsActive, int Version);

/// <summary>单条事件投递的当前快照，不包含正文、签名、密钥或原始异常文本。</summary>
/// <param name="Id">投递标识，可与接收端的投递请求头关联。</param>
/// <param name="TenantId">由原订阅权威关联得到的所属企业。</param>
/// <param name="SubscriptionId">明确所属的订阅。</param>
/// <param name="EventId">原事件标识，接收端据此去重。</param>
/// <param name="StatusKey">pending、processing、delivered 或 failed。</param>
/// <param name="AttemptCount">已完成的尝试次数，不包含正在执行的一次。</param>
/// <param name="NextAttemptAtUtc">计划下次读取的 UTC 时间；终态为空。</param>
/// <param name="LastErrorCode">上次失败的安全机器码，不返回原始异常。</param>
/// <param name="LastHttpStatus">可确认的上次非成功 HTTP 状态码；其他故障为空。</param>
/// <param name="CreatedAtUtc">原投递入队时间，不替代事件发生时间。</param>
/// <param name="UpdatedAtUtc">当前投递最近更新的 UTC 时间，不代表完整逐次尝试历史。</param>
/// <param name="Version">读取时投递修订。</param>
public sealed record WebhookDeliveryHistoryResponse(Guid Id, Guid TenantId, Guid SubscriptionId, Guid EventId,
    string StatusKey, int AttemptCount, DateTimeOffset? NextAttemptAtUtc, string? LastErrorCode, int? LastHttpStatus,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, int Version);

/// <summary>逐次领取的开始及首次结果；未确认不会被推断为失败或成功。</summary>
/// <param name="Id">尝试标识。</param>
/// <param name="TenantId">可信企业标识。</param>
/// <param name="SubscriptionId">原订阅标识。</param>
/// <param name="DeliveryId">原投递标识。</param>
/// <param name="OutcomeKey">processing、unconfirmed、delivered 或 failed。</param>
/// <param name="StartedAtUtc">开始处理时间，不保证已经发出 HTTP。</param>
/// <param name="CompletedAtUtc">首次确认结果时间，未确认时为空。</param>
/// <param name="HttpStatus">已确认的 HTTP 应答状态，非 HTTP 故障为空。</param>
/// <param name="ErrorCode">安全失败类别，不包含原始异常。</param>
/// <param name="AppliedToDelivery">该次结果是否推进原租约的当前投递状态；未确认时为空。</param>
public sealed record WebhookDeliveryAttemptResponse(Guid Id, Guid TenantId, Guid SubscriptionId, Guid DeliveryId,
    string OutcomeKey, DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc, int? HttpStatus, string? ErrorCode, bool? AppliedToDelivery);

/// <summary>
/// 单次 Webhook 投递记录响应契约；用于投递历史查询与失败排查。
/// </summary>
/// <remarks>
/// 投递所有权：LegacyPolling 与 ShadowCdc 来源写入 fn_outbox_message 表，
/// CdcKafka 来源写入 fn_messaging_outbox_event 表；Status 取值自 WebhookDeliveryStatuses。
/// </remarks>
/// <param name="Id">投递记录稳定标识。</param>
/// <param name="SubscriptionId">关联订阅标识。</param>
/// <param name="EventId">触发改投递的集成事件标识，用于跨模块追溯。</param>
/// <param name="Status">投递状态稳定机器码，取值自 WebhookDeliveryStatuses。</param>
/// <param name="AttemptCount">累计投递尝试次数，从 1 开始；超过最大次数即进入 Failed。</param>
/// <param name="Version">乐观并发版本号，用于重试与状态变更请求的 CAS 守卫。</param>
public sealed record WebhookDeliveryResponse(
    Guid Id,
    Guid SubscriptionId,
    Guid EventId,
    string Status,
    int AttemptCount,
    int Version);

/// <summary>
/// Webhook 事件信封契约；承载一次业务事件触发的投递载荷，按订阅扇出至目标端点。
/// </summary>
/// <remarks>
/// 事件流所有权由 DeliveryOwner 决定：LegacyPolling 与 ShadowCdc 投递从 fn_outbox_message 出队，
/// CdcKafka 投递从 fn_messaging_outbox_event 出队；切换所有权时必须先排空旧所有者积压。
/// </remarks>
/// <param name="EventType">事件类型稳定标识，决定订阅匹配与 Handler 路由。</param>
/// <param name="EventId">事件唯一标识，订阅方据此做幂等去重，重复投递不产生重复业务副作用。</param>
/// <param name="TenantId">事件所属租户标识；Host 级事件为 Guid.Empty。</param>
/// <param name="OccurredAtUtc">业务事件发生时间（UTC），与投递时间区分用于顺序判定。</param>
/// <param name="Data">事件强类型载荷；不同 EventType 对应不同的 Data 子类。</param>
public sealed record WebhookEventEnvelope(
    string EventType,
    Guid EventId,
    Guid TenantId,
    DateTimeOffset OccurredAtUtc,
    WebhookWorkflowInstanceCompletedData Data);

/// <summary>
/// 工作流实例完成事件的载荷契约；投递给订阅方告知实例已结束并指向业务对象。
/// </summary>
/// <remarks>
/// 该载荷为只读投影，字段顺序为稳定机器码的一部分；订阅方应基于 InstanceId 做幂等处理。
/// </remarks>
/// <param name="InstanceId">工作流实例标识，全局唯一。</param>
/// <param name="RecipientUserId">实例发起人或接收人用户标识，便于订阅方按用户聚合。</param>
/// <param name="BusinessType">关联业务对象的稳定类型键，如订单、审批等。</param>
/// <param name="BusinessId">关联业务对象的稳定标识，订阅方据此回查业务状态。</param>
public sealed record WebhookWorkflowInstanceCompletedData(
    Guid InstanceId,
    Guid RecipientUserId,
    string BusinessType,
    string BusinessId);
