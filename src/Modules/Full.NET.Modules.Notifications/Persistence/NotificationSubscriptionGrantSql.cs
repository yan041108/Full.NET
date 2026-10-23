using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Notifications.Persistence;

/// <summary>客户端同意事实的本地一次性台账；不代表微信保证的发送库存。</summary>
internal static class NotificationSubscriptionGrantSql
{
    public static readonly SqlStatement FindReport = new("notifications.subscription_grant.find_report", """
        SELECT ReportedStatusKey FROM fn_notifications_subscription_grant
        WHERE BindingId = @BindingId AND TemplateId = @TemplateId AND AuthorizationId = @AuthorizationId
        """, SqlDataScope.Global);
    public static readonly SqlStatement RevokeAvailable = new("notifications.subscription_grant.revoke_available", """
        UPDATE fn_notifications_subscription_grant
        SET StateKey = CASE WHEN StateKey = 'reserved' THEN 'revoked_reserved' ELSE 'revoked' END
        WHERE BindingId = @BindingId AND TemplateId = @TemplateId AND StateKey IN ('available', 'reserved')
        """, SqlDataScope.Global);
    public static readonly SqlStatement RevokeBinding = new("notifications.subscription_grant.revoke_binding", """
        UPDATE fn_notifications_subscription_grant
        SET StateKey = CASE WHEN StateKey = 'reserved' THEN 'revoked_reserved' ELSE 'revoked' END
        WHERE BindingId = @BindingId AND StateKey IN ('available', 'reserved')
        """, SqlDataScope.Global);
    public static readonly SqlStatement ResetBindingSubscriptions = new("notifications.subscription_grant.reset_binding_subscriptions", """
        UPDATE fn_notifications_wechat_miniprogram_subscription SET StatusKey = 'rejected'
        WHERE BindingId = @BindingId
        """, SqlDataScope.Global);
    public static readonly SqlStatement Insert = new(
        "notifications.subscription_grant.insert",
        """
        INSERT INTO fn_notifications_subscription_grant
            (Id, BindingId, TemplateId, AuthorizationId, ReportedStatusKey, StateKey, CreatedAtUtc)
        SELECT @Id, @BindingId, @TemplateId, @AuthorizationId, @ReportedStatusKey, @StateKey, @Now
        WHERE NOT EXISTS (SELECT 1 FROM fn_notifications_subscription_grant
            WHERE BindingId = @BindingId AND TemplateId = @TemplateId AND AuthorizationId = @AuthorizationId)
          AND (@StateKey = 'rejected' OR (SELECT COUNT(1) FROM fn_notifications_subscription_grant
               WHERE BindingId = @BindingId AND TemplateId = @TemplateId AND StateKey = 'available') < 100)
        """, SqlDataScope.Global);

    // Unicode 参数隐式转换会让可串行化查询退化为堆表锁；固定唯一索引与列类型保护并发预留。
    public static readonly SqlStatement LockBindingSqlServer = new(
        "notifications.subscription_grant.lock_binding.sql_server",
        """
        SELECT Id FROM fn_notifications_wechat_miniprogram_binding
            WITH (UPDLOCK, HOLDLOCK, ROWLOCK, INDEX(UX_fn_notifications_wechat_miniprogram_binding_Scope_User_App), FORCESEEK)
        WHERE TenantScopeKey = CAST(@TenantScopeKey AS varchar(64)) AND UserId = @UserId
          AND AppId = CAST(@AppId AS varchar(32))
        """, SqlDataScope.Global);

    public static readonly SqlStatement LockBindingMySql = new(
        "notifications.subscription_grant.lock_binding.my_sql",
        """
        SELECT Id FROM fn_notifications_wechat_miniprogram_binding
        WHERE TenantScopeKey = @TenantScopeKey AND UserId = @UserId AND AppId = @AppId FOR UPDATE
        """, SqlDataScope.Global);

    public static readonly SqlStatement FindAvailableSqlServer = new(
        "notifications.subscription_grant.find_available.sql_server",
        """
        SELECT TOP (1) g.Id FROM fn_notifications_subscription_grant g WITH (UPDLOCK, HOLDLOCK)
        INNER JOIN fn_notifications_wechat_miniprogram_binding b ON b.Id = g.BindingId
        INNER JOIN fn_notifications_wechat_miniprogram_subscription s ON s.BindingId = b.Id AND s.TemplateId = g.TemplateId
        WHERE b.TenantScopeKey = @TenantScopeKey AND b.UserId = @UserId AND b.AppId = @AppId
          AND b.ProviderProfileVersionId = @ProfileVersionId AND b.VerificationStatusKey = 'verified'
          AND g.TemplateId = @TemplateId AND s.StatusKey = 'accepted' AND g.StateKey = 'available'
        ORDER BY g.CreatedAtUtc, g.Id
        """, SqlDataScope.Global);

    public static readonly SqlStatement FindAvailableMySql = new(
        "notifications.subscription_grant.find_available.my_sql",
        """
        SELECT g.Id FROM fn_notifications_subscription_grant g
        INNER JOIN fn_notifications_wechat_miniprogram_binding b ON b.Id = g.BindingId
        INNER JOIN fn_notifications_wechat_miniprogram_subscription s ON s.BindingId = b.Id AND s.TemplateId = g.TemplateId
        WHERE b.TenantScopeKey = @TenantScopeKey AND b.UserId = @UserId AND b.AppId = @AppId
          AND b.ProviderProfileVersionId = @ProfileVersionId AND b.VerificationStatusKey = 'verified'
          AND g.TemplateId = @TemplateId AND s.StatusKey = 'accepted' AND g.StateKey = 'available'
        ORDER BY g.CreatedAtUtc, g.Id LIMIT 1 FOR UPDATE
        """, SqlDataScope.Global);

    public static readonly SqlStatement Reserve = new(
        "notifications.subscription_grant.reserve",
        """
        UPDATE fn_notifications_subscription_grant
        SET StateKey = 'reserved', DeliveryId = @DeliveryId, LeaseGeneration = @LeaseGeneration
        WHERE Id = @Id AND StateKey = 'available'
        """, SqlDataScope.Global);

    public static readonly SqlStatement CountUnresolved = new(
        "notifications.subscription_grant.count_unresolved",
        """
        SELECT COUNT(1) FROM fn_notifications_subscription_grant
        WHERE DeliveryId = @DeliveryId AND StateKey IN ('reserved', 'revoked_reserved', 'pending_reconciliation')
        """, SqlDataScope.Global);

    public static readonly SqlStatement Resolve = new(
        "notifications.subscription_grant.resolve",
        """
        UPDATE fn_notifications_subscription_grant
        SET StateKey = CASE WHEN @StateKey = 'available' AND StateKey = 'revoked_reserved'
            THEN 'revoked' ELSE @StateKey END
        WHERE DeliveryId = @DeliveryId AND LeaseGeneration = @LeaseGeneration AND StateKey IN ('reserved', 'revoked_reserved')
        """, SqlDataScope.Global);

    public static readonly SqlStatement SuspendAbandoned = new(
        "notifications.subscription_grant.suspend_abandoned",
        """
        UPDATE fn_notifications_subscription_grant SET StateKey = 'pending_reconciliation'
        WHERE DeliveryId = @DeliveryId AND LeaseGeneration <> @LeaseGeneration AND StateKey IN ('reserved', 'revoked_reserved')
        """, SqlDataScope.Global);

    public static readonly SqlStatement CountSendableReservation = new("notifications.subscription_grant.count_sendable", """
        SELECT COUNT(1) FROM fn_notifications_subscription_grant
        WHERE DeliveryId = @DeliveryId AND LeaseGeneration = @LeaseGeneration AND StateKey = 'reserved'
        """, SqlDataScope.Global);
}
