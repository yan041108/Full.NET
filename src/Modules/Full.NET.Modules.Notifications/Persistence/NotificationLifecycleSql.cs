using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Notifications.Persistence;

/// <summary>归档、业务撤回与外发边界分别建模，所有用户操作显式约束可信作用域。</summary>
internal static class NotificationLifecycleSql
{
    // 只有已有对应 Attempt 的回执进入补偿，避免无法关联的旧回执占满每一轮名额。
    private const string RecoverableReceipts = """
        Id, ProviderTypeKey, ProviderMessageId, ReceiptIdempotencyKey, DeliveryId,
        ExternalStatusKey, MappedStatusKey, PayloadDigest, ReceivedAtUtc, ProcessedAtUtc, ProcessStatusKey
        FROM fn_notifications_receipt r
        WHERE r.ProcessStatusKey IN ('pending_association', 'pending_apply')
          AND EXISTS (
            SELECT 1 FROM fn_notifications_delivery_attempt a
            INNER JOIN fn_notifications_delivery d ON d.Id = a.DeliveryId
            INNER JOIN fn_notifications_provider_profile_version p ON p.Id = d.ProviderProfileVersionId
            WHERE a.ProviderMessageId = r.ProviderMessageId AND p.ProviderTypeKey = r.ProviderTypeKey)
        ORDER BY r.ReceivedAtUtc, r.Id
        """;

    public static readonly SqlStatement RecoverableReceiptsSqlServer = new("notifications.lifecycle.recover_receipts.sql_server",
        "SELECT TOP (50) " + RecoverableReceipts, SqlDataScope.Global);
    public static readonly SqlStatement RecoverableReceiptsMySql = new("notifications.lifecycle.recover_receipts.my_sql",
        "SELECT " + RecoverableReceipts + " LIMIT 50", SqlDataScope.Global);

    public static readonly SqlStatement Reconcile = new("notifications.lifecycle.reconcile", """
        UPDATE fn_notifications_delivery
        SET StatusKey = @StatusKey, NextAttemptAtUtc = NULL, Revision = Revision + 1, UpdatedAtUtc = @Now
        WHERE Id = @Id AND Revision = @Revision AND StatusKey IN ('unknown', 'dead_lettered')
          AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc <= @Now)
        """, SqlDataScope.Global);
    public static readonly SqlStatement PendingQuotas = new("notifications.lifecycle.pending_quotas", """
        SELECT Id, PolicyId, DimensionKey, WindowStartUtc
        FROM fn_notifications_quota_reservation WHERE DeliveryId = @DeliveryId
          AND StateKey IN ('reserved', 'pending_reconciliation')
        """, SqlDataScope.Global);
    public static readonly SqlStatement ReconcileSubscription = new("notifications.lifecycle.reconcile_subscription", """
        UPDATE fn_notifications_subscription_grant SET StateKey = @StateKey
        WHERE DeliveryId = @DeliveryId AND StateKey IN ('reserved', 'revoked_reserved', 'pending_reconciliation')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountDeadLetters = new("notifications.lifecycle.count_dead_letters", """
        SELECT COUNT(1) FROM fn_notifications_delivery WHERE StatusKey = 'dead_lettered'
        """, SqlDataScope.Global);
    // SQL Server COUNT 返回 int；显式使用 COUNT_BIG 与两库持久化映射的 long 契约一致。
    public static readonly SqlStatement SummarySqlServer = new("notifications.lifecycle.delivery_summary.sql_server", """
        SELECT d.ChannelKey, COALESCE(p.ProviderTypeKey, 'unknown') AS ProviderTypeKey, d.StatusKey,
               COUNT_BIG(*) AS Count, MIN(d.CreatedAtUtc) AS OldestCreatedAtUtc
        FROM fn_notifications_delivery d
        INNER JOIN fn_notifications_intent i ON i.Id = d.IntentId
        LEFT JOIN fn_notifications_provider_profile_version p ON p.Id = d.ProviderProfileVersionId
        WHERE i.TenantScopeKey = @TenantScopeKey
        GROUP BY d.ChannelKey, p.ProviderTypeKey, d.StatusKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement Summary = new("notifications.lifecycle.delivery_summary", """
        SELECT d.ChannelKey, COALESCE(p.ProviderTypeKey, 'unknown') AS ProviderTypeKey, d.StatusKey,
               COUNT(*) AS Count, MIN(d.CreatedAtUtc) AS OldestCreatedAtUtc
        FROM fn_notifications_delivery d
        INNER JOIN fn_notifications_intent i ON i.Id = d.IntentId
        LEFT JOIN fn_notifications_provider_profile_version p ON p.Id = d.ProviderProfileVersionId
        WHERE i.TenantScopeKey = @TenantScopeKey
        GROUP BY d.ChannelKey, p.ProviderTypeKey, d.StatusKey
        """, SqlDataScope.Global);

    public static readonly SqlStatement DeadLetter = new("notifications.lifecycle.dead_letter", """
        UPDATE fn_notifications_delivery
        SET StatusKey = 'dead_lettered', NextAttemptAtUtc = NULL, Revision = Revision + 1, UpdatedAtUtc = @Now
        WHERE Id = @Id AND Revision = @Revision AND StatusKey IN ('failed', 'unknown')
          AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc <= @Now)
        """, SqlDataScope.Global);

    public static readonly SqlStatement Archive = new("notifications.lifecycle.archive", """
        UPDATE fn_notifications_inbox_message SET ArchivedAtUtc = @Now
        WHERE TenantScopeKey = @TenantScopeKey AND RecipientUserId = @UserId
          AND Id IN @Ids AND ArchivedAtUtc IS NULL
        """, SqlDataScope.Global);

    public static readonly SqlStatement SetInboxMetadata = new("notifications.lifecycle.set_inbox_metadata", """
        UPDATE fn_notifications_inbox_message
        SET ProducerKey = @ProducerKey, SceneKey = @SceneKey, DeepLinkJson = @DeepLinkJson
        WHERE Id = @Id AND TenantScopeKey = @TenantScopeKey
        """, SqlDataScope.Global);

    public static readonly SqlStatement LockIntentSqlServer = new("notifications.lifecycle.lock_intent.sql_server", """
        SELECT Id FROM fn_notifications_intent WITH (UPDLOCK, HOLDLOCK)
        WHERE Id = @Id AND TenantScopeKey = @TenantScopeKey AND StatusKey <> 'revoked'
        """, SqlDataScope.Global);

    public static readonly SqlStatement LockIntentMySql = new("notifications.lifecycle.lock_intent.my_sql", """
        SELECT Id FROM fn_notifications_intent
        WHERE Id = @Id AND TenantScopeKey = @TenantScopeKey AND StatusKey <> 'revoked' FOR UPDATE
        """, SqlDataScope.Global);

    public static readonly SqlStatement EnterSendBoundary = new("notifications.lifecycle.enter_send_boundary", """
        UPDATE fn_notifications_delivery SET DispatchLeaseGeneration = @LeaseGeneration
        WHERE Id = @Id AND IntentId = @IntentId AND Revision = @Revision
          AND LeaseGeneration = @LeaseGeneration AND LeaseOwnerKey = @LeaseOwnerKey AND LeaseExpiresAtUtc > @Now
          AND StatusKey IN ('accepted', 'unknown')
        """, SqlDataScope.Global);

    public static readonly SqlStatement RevokeIntent = new("notifications.lifecycle.revoke_intent", """
        UPDATE fn_notifications_intent SET StatusKey = 'revoked', Revision = Revision + 1
        WHERE Id = @Id AND TenantScopeKey = @TenantScopeKey AND Revision = @Revision AND StatusKey <> 'revoked'
        """, SqlDataScope.Global);

    public static readonly SqlStatement StopDeliveries = new("notifications.lifecycle.stop_deliveries", """
        UPDATE fn_notifications_delivery
        SET StatusKey = 'suppressed', SuppressionReasonCode = 'notifications.intent_revoked',
            NextAttemptAtUtc = NULL, LeaseOwnerKey = NULL, LeaseExpiresAtUtc = NULL, Revision = Revision + 1
        WHERE IntentId = @IntentId AND StatusKey = 'accepted'
          AND (LeaseOwnerKey IS NULL OR DispatchLeaseGeneration IS NULL OR DispatchLeaseGeneration <> LeaseGeneration)
        """, SqlDataScope.Global);

    public static readonly SqlStatement RevokedUnusedQuotas = new("notifications.lifecycle.revoked_unused_quotas", """
        SELECT r.Id, r.PolicyId, r.DimensionKey, r.WindowStartUtc
        FROM fn_notifications_quota_reservation r
        INNER JOIN fn_notifications_delivery d ON d.Id = r.DeliveryId AND d.LeaseGeneration = r.LeaseGeneration
        WHERE d.IntentId = @IntentId AND d.StatusKey = 'suppressed'
          AND d.SuppressionReasonCode = 'notifications.intent_revoked' AND r.StateKey = 'reserved'
          AND (d.DispatchLeaseGeneration IS NULL OR d.DispatchLeaseGeneration <> d.LeaseGeneration)
        """, SqlDataScope.Global);

    public static readonly SqlStatement ReleaseRevokedUnusedSubscriptions = new("notifications.lifecycle.release_revoked_unused_subscriptions", """
        UPDATE fn_notifications_subscription_grant
        SET StateKey = CASE WHEN StateKey = 'revoked_reserved' THEN 'revoked' ELSE 'available' END
        WHERE StateKey IN ('reserved', 'revoked_reserved') AND EXISTS (
            SELECT 1 FROM fn_notifications_delivery d
            WHERE d.Id = fn_notifications_subscription_grant.DeliveryId
              AND d.LeaseGeneration = fn_notifications_subscription_grant.LeaseGeneration
              AND d.IntentId = @IntentId AND d.StatusKey = 'suppressed'
              AND d.SuppressionReasonCode = 'notifications.intent_revoked'
              AND (d.DispatchLeaseGeneration IS NULL OR d.DispatchLeaseGeneration <> d.LeaseGeneration))
        """, SqlDataScope.Global);

    public static readonly SqlStatement CountCrossed = new("notifications.lifecycle.count_crossed", """
        SELECT COUNT(1) FROM fn_notifications_delivery
        WHERE IntentId = @IntentId AND DispatchLeaseGeneration IS NOT NULL
        """, SqlDataScope.Global);

    public static readonly SqlStatement RevokeInbox = new("notifications.lifecycle.revoke_inbox", """
        UPDATE fn_notifications_inbox_message SET RevokedAtUtc = @Now, RevocationReason = @Reason
        WHERE IntentId = @IntentId AND TenantScopeKey = @TenantScopeKey AND RevokedAtUtc IS NULL
        """, SqlDataScope.Global);
}
