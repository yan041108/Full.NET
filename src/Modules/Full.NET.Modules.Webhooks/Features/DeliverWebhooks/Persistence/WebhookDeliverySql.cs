using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Webhooks.Features.DeliverWebhooks.Persistence;

internal static class WebhookDeliverySql
{
    public static readonly SqlStatement ListActiveSubscriptionsByEventType = new(
        "webhooks.subscriptions.list_active_by_event_type",
        """
        SELECT Id, TenantId, EventType, TargetUrl, SigningSecretHash, IsActive, Version
        FROM fn_webhooks_subscription
        WHERE TenantId = @TenantId
          AND EventType = @EventType
          AND IsActive = 1
        ORDER BY Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertDelivery = new(
        "webhooks.deliveries.insert",
        """
        INSERT INTO fn_webhooks_delivery
            (Id, SubscriptionId, EventId, PayloadBody, PayloadDigest, Status,
             AttemptCount, NextAttemptAtUtc, LastError, CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @SubscriptionId, @EventId, @PayloadBody, @PayloadDigest, @Status,
             0, @NextAttemptAtUtc, NULL, @CreatedAtUtc, @UpdatedAtUtc, 1)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ClaimPendingSqlServer = new(
        "webhooks.deliveries.claim_pending.sql_server",
        """
        SELECT TOP (@BatchSize)
               delivery.Id,
               delivery.SubscriptionId,
               delivery.EventId,
               delivery.PayloadBody,
               delivery.AttemptCount,
               delivery.Version,
               subscription.TenantId,
               subscription.TargetUrl,
               subscription.SigningSecretHash,
               subscription.EventType
        FROM fn_webhooks_delivery AS delivery WITH (UPDLOCK, ROWLOCK, READPAST)
        INNER JOIN fn_webhooks_subscription AS subscription
            ON subscription.Id = delivery.SubscriptionId
        WHERE delivery.Status = @PendingStatus
          AND subscription.IsActive = 1
          AND (delivery.NextAttemptAtUtc IS NULL OR delivery.NextAttemptAtUtc <= @Now)
        ORDER BY delivery.CreatedAtUtc, delivery.Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ClaimPendingMySql = new(
        "webhooks.deliveries.claim_pending.mysql",
        """
        SELECT delivery.Id,
               delivery.SubscriptionId,
               delivery.EventId,
               delivery.PayloadBody,
               delivery.AttemptCount,
               delivery.Version,
               subscription.TenantId,
               subscription.TargetUrl,
               subscription.SigningSecretHash,
               subscription.EventType
        FROM fn_webhooks_delivery AS delivery
        INNER JOIN fn_webhooks_subscription AS subscription
            ON subscription.Id = delivery.SubscriptionId
        WHERE delivery.Status = @PendingStatus
          AND subscription.IsActive = 1
          AND (delivery.NextAttemptAtUtc IS NULL OR delivery.NextAttemptAtUtc <= @Now)
        ORDER BY delivery.CreatedAtUtc, delivery.Id
        LIMIT @BatchSize
        FOR UPDATE SKIP LOCKED
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement MarkDelivered = new(
        "webhooks.deliveries.mark_delivered",
        """
        UPDATE fn_webhooks_delivery
        SET Status = @Status,
            AttemptCount = AttemptCount + 1,
            NextAttemptAtUtc = NULL,
            LastError = NULL,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @DeliveryId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement MarkFailed = new(
        "webhooks.deliveries.mark_failed",
        """
        UPDATE fn_webhooks_delivery
        SET Status = @Status,
            AttemptCount = AttemptCount + 1,
            NextAttemptAtUtc = @NextAttemptAtUtc,
            LastError = @LastError,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @DeliveryId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);
}

internal sealed record WebhookSubscriptionDeliveryRecord(
    Guid Id,
    Guid TenantId,
    string EventType,
    string TargetUrl,
    string SigningSecretHash,
    bool IsActive,
    int Version);

internal sealed record WebhookDeliveryWorkItem(
    Guid Id,
    Guid SubscriptionId,
    Guid EventId,
    string PayloadBody,
    int AttemptCount,
    int Version,
    Guid TenantId,
    string TargetUrl,
    string SigningSecretHash,
    string EventType);
