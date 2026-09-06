using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Notifications.Persistence;

/// <summary>微信小程序 OpenId 绑定与订阅授权表的显式 SQL。</summary>
internal static class WeChatMiniProgramBindingSql
{
    public static readonly SqlStatement CountForScope = new(
        "notifications.wechat_miniprogram_binding.count_for_scope",
        """
        SELECT COUNT(1)
        FROM fn_notifications_wechat_miniprogram_binding
        WHERE TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListForScopeSqlServer = new(
        "notifications.wechat_miniprogram_binding.list_for_scope.sql_server",
        """
        SELECT Id, TenantScopeKey, UserId, AppId, ProviderProfileVersionId, OpenIdProtected,
               OpenIdMask, OpenIdSha256Hex, UnionIdProtected, UnionIdMask, VerificationStatusKey,
               RecipientEndpointId, CreatedAtUtc, UpdatedAtUtc
        FROM fn_notifications_wechat_miniprogram_binding
        WHERE TenantScopeKey = @TenantScopeKey
        ORDER BY CreatedAtUtc DESC, Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListForScopeMySql = new(
        "notifications.wechat_miniprogram_binding.list_for_scope.mysql",
        """
        SELECT Id, TenantScopeKey, UserId, AppId, ProviderProfileVersionId, OpenIdProtected,
               OpenIdMask, OpenIdSha256Hex, UnionIdProtected, UnionIdMask, VerificationStatusKey,
               RecipientEndpointId, CreatedAtUtc, UpdatedAtUtc
        FROM fn_notifications_wechat_miniprogram_binding
        WHERE TenantScopeKey = @TenantScopeKey
        ORDER BY CreatedAtUtc DESC, Id DESC
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListMineForScope = new(
        "notifications.wechat_miniprogram_binding.list_mine_for_scope",
        """
        SELECT Id, TenantScopeKey, UserId, AppId, ProviderProfileVersionId, OpenIdProtected,
               OpenIdMask, OpenIdSha256Hex, UnionIdProtected, UnionIdMask, VerificationStatusKey,
               RecipientEndpointId, CreatedAtUtc, UpdatedAtUtc
        FROM fn_notifications_wechat_miniprogram_binding
        WHERE TenantScopeKey = @TenantScopeKey
          AND UserId = @UserId
          AND VerificationStatusKey <> @Revoked
        ORDER BY CreatedAtUtc DESC, Id DESC
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindById = new(
        "notifications.wechat_miniprogram_binding.find_by_id",
        """
        SELECT Id, TenantScopeKey, UserId, AppId, ProviderProfileVersionId, OpenIdProtected,
               OpenIdMask, OpenIdSha256Hex, UnionIdProtected, UnionIdMask, VerificationStatusKey,
               RecipientEndpointId, CreatedAtUtc, UpdatedAtUtc
        FROM fn_notifications_wechat_miniprogram_binding
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindByUserApp = new(
        "notifications.wechat_miniprogram_binding.find_by_user_app",
        """
        SELECT Id, TenantScopeKey, UserId, AppId, ProviderProfileVersionId, OpenIdProtected,
               OpenIdMask, OpenIdSha256Hex, UnionIdProtected, UnionIdMask, VerificationStatusKey,
               RecipientEndpointId, CreatedAtUtc, UpdatedAtUtc
        FROM fn_notifications_wechat_miniprogram_binding
        WHERE TenantScopeKey = @TenantScopeKey
          AND UserId = @UserId
          AND AppId = @AppId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement Insert = new(
        "notifications.wechat_miniprogram_binding.insert",
        """
        INSERT INTO fn_notifications_wechat_miniprogram_binding
            (Id, TenantScopeKey, UserId, AppId, ProviderProfileVersionId, OpenIdProtected,
             OpenIdMask, OpenIdSha256Hex, UnionIdProtected, UnionIdMask, VerificationStatusKey,
             RecipientEndpointId, CreatedAtUtc, UpdatedAtUtc)
        VALUES
            (@Id, @TenantScopeKey, @UserId, @AppId, @ProviderProfileVersionId, @OpenIdProtected,
             @OpenIdMask, @OpenIdSha256Hex, @UnionIdProtected, @UnionIdMask, @VerificationStatusKey,
             @RecipientEndpointId, @CreatedAtUtc, @UpdatedAtUtc)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement Update = new(
        "notifications.wechat_miniprogram_binding.update",
        """
        UPDATE fn_notifications_wechat_miniprogram_binding
        SET OpenIdProtected = @OpenIdProtected,
            OpenIdMask = @OpenIdMask,
            OpenIdSha256Hex = @OpenIdSha256Hex,
            UnionIdProtected = @UnionIdProtected,
            UnionIdMask = @UnionIdMask,
            VerificationStatusKey = @VerificationStatusKey,
            ProviderProfileVersionId = @ProviderProfileVersionId,
            RecipientEndpointId = @RecipientEndpointId,
            UpdatedAtUtc = @UpdatedAtUtc
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListSubscriptionsByBinding = new(
        "notifications.wechat_miniprogram_subscription.list_by_binding",
        """
        SELECT BindingId, TemplateId, StatusKey, AuthorizedAtUtc
        FROM fn_notifications_wechat_miniprogram_subscription
        WHERE BindingId = @BindingId
        ORDER BY AuthorizedAtUtc DESC, TemplateId ASC
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement UpsertSubscription = new(
        "notifications.wechat_miniprogram_subscription.upsert",
        """
        MERGE fn_notifications_wechat_miniprogram_subscription AS target
        USING (SELECT @BindingId AS BindingId, @TemplateId AS TemplateId) AS source
        ON target.BindingId = source.BindingId AND target.TemplateId = source.TemplateId
        WHEN MATCHED THEN
            UPDATE SET StatusKey = @StatusKey, AuthorizedAtUtc = @AuthorizedAtUtc
        WHEN NOT MATCHED THEN
            INSERT (BindingId, TemplateId, StatusKey, AuthorizedAtUtc)
            VALUES (@BindingId, @TemplateId, @StatusKey, @AuthorizedAtUtc);
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement UpsertSubscriptionMySql = new(
        "notifications.wechat_miniprogram_subscription.upsert.mysql",
        """
        INSERT INTO fn_notifications_wechat_miniprogram_subscription
            (BindingId, TemplateId, StatusKey, AuthorizedAtUtc)
        VALUES
            (@BindingId, @TemplateId, @StatusKey, @AuthorizedAtUtc)
        ON DUPLICATE KEY UPDATE
            StatusKey = VALUES(StatusKey),
            AuthorizedAtUtc = VALUES(AuthorizedAtUtc)
        """,
        SqlDataScope.Global);
}
