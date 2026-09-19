namespace Full.NET.Modules.Webhooks.Contracts;

/// <summary>
/// Webhook 模块稳定权限码集合；不可本地化，作为服务端授权与客户端可见性的共同权威。
/// </summary>
public static class WebhookPermissions
{
    /// <summary>允许读取 Webhook 订阅列表与详情，以及最近投递记录。</summary>
    public const string SubscriptionsRead = "webhooks.subscriptions.read";

    /// <summary>允许创建、更新、删除 Webhook 订阅，含 SigningSecret 重置与启用/停用。</summary>
    public const string SubscriptionsManage = "webhooks.subscriptions.manage";
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
    /// <summary>模块错误码通用前缀；所有具体错误码以前缀 + '.' + 后缀拼接，避免跨模块冲突。</summary>
    public const string Prefix = "webhooks.";

    /// <summary>指定订阅未找到；订阅标识不存在或已被删除。</summary>
    public const string SubscriptionNotFound = "webhooks.subscription.not_found";

    /// <summary>订阅目标 URL 非法：未通过 HTTPS、主机名解析或回调地址校验。</summary>
    public const string TargetUrlInvalid = "webhooks.subscription.target_url_invalid";
}

/// <summary>
/// Webhook 订阅响应契约；字段顺序为稳定机器码的一部分，新增可选字段只能追加。
/// </summary>
/// <remarks>
/// SigningSecret 不在响应中回显，避免向客户端泄漏签名密钥；客户端只能通过管理端点重置。
/// </remarks>
/// <param name="Id">订阅稳定标识。</param>
/// <param name="TenantId">所属租户标识；Host 级订阅为 Guid.Empty。</param>
/// <param name="EventType">订阅的事件类型稳定标识（CLR FullName 或目录登记键）。</param>
/// <param name="TargetUrl">接收投递的 HTTPS 端点绝对地址。</param>
/// <param name="IsActive">订阅是否处于启用状态；停用后不再产生新的投递。</param>
/// <param name="Version">乐观并发版本号，用于更新、删除与启用/停用请求的 CAS 守卫。</param>
public sealed record WebhookSubscriptionResponse(
    Guid Id,
    Guid TenantId,
    string EventType,
    string TargetUrl,
    bool IsActive,
    int Version);

/// <summary>
/// 创建 Webhook 订阅的请求契约；SigningSecret 由服务端哈希存储，响应永不回显明文。
/// </summary>
/// <remarks>
/// 同一 (TenantId, EventType, TargetUrl) 三元组应保持唯一；幂等键由调用方提供以避免重复创建。
/// </remarks>
/// <param name="EventType">订阅的事件类型稳定标识。</param>
/// <param name="TargetUrl">接收投递的 HTTPS 端点绝对地址；必须通过 URL 合法性校验。</param>
/// <param name="SigningSecret">用于签名投递负载的密钥明文；服务端存储哈希，明文不持久化。</param>
public sealed record CreateWebhookSubscriptionRequest(
    string EventType,
    string TargetUrl,
    string SigningSecret);

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