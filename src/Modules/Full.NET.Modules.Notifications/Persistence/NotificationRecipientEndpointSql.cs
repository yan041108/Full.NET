using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Notifications.Persistence;

/// <summary>Provider 专属收件端点 SQL；列表投影故意省略 ProtectedValue。</summary>
internal static class NotificationRecipientEndpointSql
{
    /// <summary>
    /// 查询当前作用域仍处于启用状态的最新发布 Profile 版本，并返回 ProviderTypeKey。
    /// </summary>
    public static readonly SqlStatement FindPublishedProviderTypeForScope = new(
        "notifications.recipient_endpoint.find_published_provider_type_for_scope",
        """
        SELECT pv.ProviderTypeKey
        FROM fn_notifications_provider_profile_version pv
        INNER JOIN fn_notifications_provider_profile p ON p.Id = pv.ProfileId
        WHERE pv.Id = @ProviderProfileVersionId
          AND p.TenantScopeKey = @TenantScopeKey
          AND p.LatestPublishedVersionId = pv.Id
          AND p.IsEnabled = 1
        """,
        SqlDataScope.Global);

    /// <summary>
    /// SQL Server 在唯一键范围上取得更新锁和范围锁，避免并发检查后同时插入。
    /// </summary>
    public static readonly SqlStatement LockExistingSqlServer = new(
        "notifications.recipient_endpoint.lock_existing.sqlserver",
        """
        SELECT Id
        FROM fn_notifications_recipient_endpoint WITH (UPDLOCK, HOLDLOCK)
        WHERE TenantScopeKey = @TenantScopeKey
          AND UserId = @UserId
          AND ProviderProfileVersionId = @ProviderProfileVersionId
          AND EndpointKindKey = @EndpointKindKey
        """,
        SqlDataScope.Global);

    /// <summary>
    /// MySQL 在唯一键范围上取得 next-key 锁，避免并发检查后同时插入。
    /// </summary>
    public static readonly SqlStatement LockExistingMySql = new(
        "notifications.recipient_endpoint.lock_existing.mysql",
        """
        SELECT Id
        FROM fn_notifications_recipient_endpoint
        WHERE TenantScopeKey = @TenantScopeKey
          AND UserId = @UserId
          AND ProviderProfileVersionId = @ProviderProfileVersionId
          AND EndpointKindKey = @EndpointKindKey
        FOR UPDATE
        """,
        SqlDataScope.Global);

    /// <summary>插入已经过作用域、Profile 和端点类型验证的受保护端点。</summary>
    public static readonly SqlStatement Insert = new(
        "notifications.recipient_endpoint.insert",
        """
        INSERT INTO fn_notifications_recipient_endpoint
            (Id, TenantId, ScopeKey, TenantScopeKey, UserId, ProviderProfileVersionId,
             EndpointKindKey, ProtectedValue, MaskedValue, VerificationStatusKey, CreatedAtUtc, UpdatedAtUtc)
        VALUES
            (@Id, @InboxTenantId, @ScopeKey, @TenantScopeKey, @UserId, @ProviderProfileVersionId,
             @EndpointKindKey, @ProtectedValue, @MaskedValue, @VerificationStatusKey, @CreatedAtUtc, NULL)
        """,
        SqlDataScope.Global);

    /// <summary>只删除当前作用域中属于当前用户的精确端点。</summary>
    public static readonly SqlStatement DeleteMine = new(
        "notifications.recipient_endpoint.delete_mine",
        """
        DELETE FROM fn_notifications_recipient_endpoint
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND UserId = @UserId
        """,
        SqlDataScope.Global);

    /// <summary>按当前受信作用域和用户列出脱敏端点，不投影 ProtectedValue。</summary>
    public static readonly SqlStatement ListMaskedByScopeUser = new(
        "notifications.recipient_endpoint.list_masked",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, UserId, ProviderProfileVersionId,
               EndpointKindKey, MaskedValue, VerificationStatusKey, CreatedAtUtc, UpdatedAtUtc
        FROM fn_notifications_recipient_endpoint
        WHERE TenantScopeKey = @TenantScopeKey
          AND UserId = @UserId
        ORDER BY EndpointKindKey, Id
        """,
        SqlDataScope.Global);

    /// <summary>按标识和作用域读取刚写入的脱敏端点。</summary>
    public static readonly SqlStatement FindMaskedById = new(
        "notifications.recipient_endpoint.find_masked",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, UserId, ProviderProfileVersionId,
               EndpointKindKey, MaskedValue, VerificationStatusKey, CreatedAtUtc, UpdatedAtUtc
        FROM fn_notifications_recipient_endpoint
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    /// <summary>Worker 只读取精确 Profile 版本下已验证端点的受保护值。</summary>
    public static readonly SqlStatement FindVerifiedProtectedForDelivery = new(
        "notifications.recipient_endpoint.find_verified_protected_for_delivery",
        """
        SELECT ProtectedValue
        FROM fn_notifications_recipient_endpoint
        WHERE TenantScopeKey = @TenantScopeKey
          AND UserId = @UserId
          AND ProviderProfileVersionId = @ProviderProfileVersionId
          AND EndpointKindKey = @EndpointKindKey
          AND VerificationStatusKey = 'verified'
        """,
        SqlDataScope.Global);
}
