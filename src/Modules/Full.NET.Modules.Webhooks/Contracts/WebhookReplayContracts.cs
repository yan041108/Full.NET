namespace Full.NET.Modules.Webhooks.Contracts;

/// <summary>明确确认原失败投递后重新排队，保留次数并沿用当前有界投递策略；重复操作返回同一原回执。</summary>
/// <param name="OperationId">客户端生成并保留的非空操作标识，未知结果必须沿用。</param>
/// <param name="Version">确认时读取的原终态失败投递修订。</param>
public sealed record ReplayWebhookDeliveryRequest(Guid OperationId, int Version);

/// <summary>不可变的原重放排队事实，不代表接收端成功或当前投递仍在等待。</summary>
/// <param name="OperationId">原操作标识。</param>
/// <param name="TenantId">可信企业标识。</param>
/// <param name="SubscriptionId">原订阅标识。</param>
/// <param name="DeliveryId">原投递标识。</param>
/// <param name="EventId">保持不变的业务事件身份。</param>
/// <param name="PreviousVersion">排队前原失败修订。</param>
/// <param name="Version">排队成功后的修订。</param>
/// <param name="AttemptCount">排队时保留的已完成次数，不重新归零。</param>
/// <param name="QueuedAtUtc">本地事务排队时间。</param>
public sealed record WebhookDeliveryReplayReceipt(Guid OperationId, Guid TenantId, Guid SubscriptionId, Guid DeliveryId, Guid EventId,
    int PreviousVersion, int Version, int AttemptCount, DateTimeOffset QueuedAtUtc);
