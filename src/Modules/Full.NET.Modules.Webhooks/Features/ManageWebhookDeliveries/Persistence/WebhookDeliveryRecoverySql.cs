using Full.NET.Data.Abstractions;
using Full.NET.Modules.Webhooks.Contracts;

namespace Full.NET.Modules.Webhooks.Features.ManageWebhookDeliveries.Persistence;

/// <summary>外部已提交事实与本地待发送状态同事务收敛。</summary>
internal static class WebhookDeliveryRecoverySql
{
    internal static readonly SqlStatement Find = new("webhooks.recovery.find", """
        SELECT TenantId,OperationId,ActorUserId,SubscriptionId,DeliveryId,EventId,PreviousVersion,
               Version,AttemptCount,PayloadSha256,ReceiverReceiptSha256,ConfirmedAtUtc
        FROM fn_webhooks_delivery_recovery WHERE TenantId=@TenantId AND OperationId=@OperationId
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement Confirm = new("webhooks.recovery.confirm", """
        UPDATE fn_webhooks_delivery
        SET Status=@DeliveredStatus,NextAttemptAtUtc=NULL,LastError=NULL,UpdatedAtUtc=@ConfirmedAtUtc,Version=Version+1
        WHERE Id=@DeliveryId AND SubscriptionId=@SubscriptionId AND EventId=@EventId
          AND Version=@PreviousVersion AND Status=@PendingStatus AND PayloadDigest=@PayloadSha256
          AND EXISTS (SELECT 1 FROM fn_webhooks_subscription subscription
            WHERE subscription.Id=@SubscriptionId AND subscription.TenantId=@TenantId)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement Insert = new("webhooks.recovery.insert", """
        INSERT INTO fn_webhooks_delivery_recovery
            (Id,TenantId,OperationId,ActorUserId,ActorSessionId,SubscriptionId,DeliveryId,EventId,
             PreviousVersion,Version,AttemptCount,PayloadSha256,ReceiverReceiptSha256,ConfirmedAtUtc)
        VALUES (@Id,@TenantId,@OperationId,@ActorUserId,@ActorSessionId,@SubscriptionId,@DeliveryId,@EventId,
                @PreviousVersion,@Version,@AttemptCount,@PayloadSha256,@ReceiverReceiptSha256,@ConfirmedAtUtc)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}

/// <summary>审计持久化仅记录证据摘要和原身份。</summary>
internal sealed record WebhookDeliveryRecoveryRecord(Guid TenantId, Guid OperationId, Guid ActorUserId,
    Guid SubscriptionId, Guid DeliveryId, Guid EventId, int PreviousVersion, int Version, int AttemptCount,
    string PayloadSha256, string ReceiverReceiptSha256, DateTimeOffset ConfirmedAtUtc)
{
    internal WebhookDeliveryRecoveryReceipt ToReceipt() => new(OperationId, TenantId, SubscriptionId, DeliveryId,
        EventId, PreviousVersion, Version, AttemptCount, PayloadSha256, ReceiverReceiptSha256, ConfirmedAtUtc);
}
