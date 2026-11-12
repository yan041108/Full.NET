namespace Full.NET.Modules.Webhooks.Contracts;

/// <summary>对账确认绑定原投递修订及接收方权威证据的摘要。</summary>
public sealed record ConfirmWebhookDeliveryRequest(Guid OperationId, int Version, Guid EventId,
    string PayloadSha256, string ReceiverReceiptSha256);

/// <summary>已提交的恢复对账回执。尝试次数仅统计发送方实际发起的尝试。</summary>
public sealed record WebhookDeliveryRecoveryReceipt(Guid OperationId, Guid TenantId, Guid SubscriptionId,
    Guid DeliveryId, Guid EventId, int PreviousVersion, int Version, int AttemptCount,
    string PayloadSha256, string ReceiverReceiptSha256, DateTimeOffset ConfirmedAtUtc);
