using Full.NET.Data.Abstractions;

/// <summary>Webhooks 模块显式 SQL 语句；参数化访问，作用域在声明处标注。</summary>
namespace Full.NET.Modules.Webhooks.Features.DeliverWebhooks.Persistence;

internal static class WebhookDeliverySql
{
    public static readonly SqlStatement ListTargetIdsSqlServer = new(
        "webhooks.subscriptions.target_ids.sql_server",
        """
        SELECT TOP (501) Id FROM fn_webhooks_subscription
        WHERE TenantId = @TenantId AND EventType = @EventType AND IsActive = 1 ORDER BY Id
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement ListTargetIdsMySql = new(
        "webhooks.subscriptions.target_ids.mysql",
        """
        SELECT Id FROM fn_webhooks_subscription
        WHERE TenantId = @TenantId AND EventType = @EventType AND IsActive = 1 ORDER BY Id LIMIT 501
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement InsertDeliveryBatchSqlServer = new(
        "webhooks.deliveries.insert_batch.sql_server",
        """
        INSERT INTO fn_webhooks_delivery
            (Id, SubscriptionId, EventId, PayloadBody, PayloadDigest, Status,
             AttemptCount, NextAttemptAtUtc, LastError, CreatedAtUtc, UpdatedAtUtc, Version)
        SELECT rowData.Id, rowData.SubscriptionId, @EventId, @PayloadBody, @PayloadDigest, @Status,
               0, @NowUtc, NULL, @NowUtc, @NowUtc, 1
        FROM OPENJSON(@Rows) WITH (Id uniqueidentifier '$.id', SubscriptionId uniqueidentifier '$.subscriptionId') rowData
        WHERE NOT EXISTS (SELECT 1 FROM fn_webhooks_delivery existing WITH (UPDLOCK, HOLDLOCK)
            WHERE existing.EventId = @EventId AND existing.SubscriptionId = rowData.SubscriptionId)
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement InsertDeliveryBatchMySql = new(
        "webhooks.deliveries.insert_batch.mysql",
        """
        INSERT INTO fn_webhooks_delivery
            (Id, SubscriptionId, EventId, PayloadBody, PayloadDigest, Status,
             AttemptCount, NextAttemptAtUtc, LastError, CreatedAtUtc, UpdatedAtUtc, Version)
        SELECT UNHEX(REPLACE(rowData.Id, '-', '')), UNHEX(REPLACE(rowData.SubscriptionId, '-', '')),
               @EventId, @PayloadBody, @PayloadDigest, @Status, 0, @NowUtc, NULL, @NowUtc, @NowUtc, 1
        FROM JSON_TABLE(@Rows, '$[*]' COLUMNS (Id CHAR(36) PATH '$.id', SubscriptionId CHAR(36) PATH '$.subscriptionId')) rowData
        ON DUPLICATE KEY UPDATE Id = fn_webhooks_delivery.Id
        """, SqlDataScope.HostOnly);

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
        WHERE (delivery.Status = @PendingStatus OR (delivery.Status = 'processing' AND delivery.UpdatedAtUtc <= @ExpiredBefore))
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
        WHERE (delivery.Status = @PendingStatus OR (delivery.Status = 'processing' AND delivery.UpdatedAtUtc <= @ExpiredBefore))
          AND subscription.IsActive = 1
          AND (delivery.NextAttemptAtUtc IS NULL OR delivery.NextAttemptAtUtc <= @Now)
        ORDER BY delivery.CreatedAtUtc, delivery.Id
        LIMIT @BatchSize
        FOR UPDATE SKIP LOCKED
        """,
        SqlDataScope.HostOnly);

    /// <summary>在持锁事务中领取，版本号同时作为过期恢复后的执行隔离标识。</summary>
    public static readonly SqlStatement Claim = new(
        "webhooks.deliveries.claim",
        """
        UPDATE fn_webhooks_delivery
        SET Status = 'processing', UpdatedAtUtc = @Now, Version = Version + 1
        WHERE Id = @DeliveryId AND Version = @Version
          AND (Status = 'pending' OR (Status = 'processing' AND UpdatedAtUtc <= @ExpiredBefore))
        """, SqlDataScope.HostOnly);

    public static readonly SqlStatement RenewLease = new(
        "webhooks.deliveries.renew_lease",
        """
        UPDATE fn_webhooks_delivery SET UpdatedAtUtc = @Now
        WHERE Id = @DeliveryId AND Version = @Version AND Status = 'processing'
          AND UpdatedAtUtc > @ExpiredBefore
        """, SqlDataScope.HostOnly);

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
          AND Version = @Version AND Status = 'processing'
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
          AND Version = @Version AND Status = 'processing'
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
