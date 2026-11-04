using Full.NET.Data.Abstractions;
using Full.NET.Modules.Webhooks.Contracts;

namespace Full.NET.Modules.Webhooks.Features.ManageWebhookDeliveries.Persistence;

/// <summary>只在本企业原投递上保留重放事实；不复制事件、不重写正文或重置历史次数。</summary>
internal static class WebhookDeliveryReplaySql
{
    internal static readonly SqlStatement Find = new("webhooks.replays.find", """
        SELECT TenantId,OperationId,ActorUserId,ActorSessionId,SubscriptionId,DeliveryId,EventId,PreviousVersion,Version,AttemptCount,QueuedAtUtc
        FROM fn_webhooks_delivery_replay WHERE TenantId=@TenantId AND OperationId=@OperationId
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Queue = new("webhooks.replays.queue", """
        UPDATE fn_webhooks_delivery
        SET Status='Pending',NextAttemptAtUtc=@QueuedAtUtc,UpdatedAtUtc=@QueuedAtUtc,Version=Version+1
        WHERE Id=@DeliveryId AND SubscriptionId=@SubscriptionId AND Version=@PreviousVersion AND Status='Failed'
          AND EXISTS (SELECT 1 FROM fn_webhooks_subscription subscription
            WHERE subscription.Id=@SubscriptionId AND subscription.TenantId=@TenantId AND subscription.IsActive=1)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement Insert = new("webhooks.replays.insert", """
        INSERT INTO fn_webhooks_delivery_replay
            (Id,TenantId,OperationId,ActorUserId,ActorSessionId,SubscriptionId,DeliveryId,EventId,PreviousVersion,Version,AttemptCount,QueuedAtUtc)
        VALUES (@Id,@TenantId,@OperationId,@ActorUserId,@ActorSessionId,@SubscriptionId,@DeliveryId,@EventId,@PreviousVersion,@Version,@AttemptCount,@QueuedAtUtc)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}

/// <summary>原重放回执也是 B0 领域审计；用户和会话留在域内，不含秘密或正文。</summary>
internal sealed record WebhookDeliveryReplayRecord(Guid TenantId, Guid OperationId, Guid ActorUserId, Guid ActorSessionId,
    Guid SubscriptionId, Guid DeliveryId, Guid EventId, int PreviousVersion, int Version, int AttemptCount, DateTimeOffset QueuedAtUtc)
{
    internal WebhookDeliveryReplayReceipt ToReceipt() => new(OperationId, TenantId, SubscriptionId, DeliveryId, EventId, PreviousVersion, Version, AttemptCount, QueuedAtUtc);
}
