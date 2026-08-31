using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Notifications.Persistence;

/// <summary>Provider 专属收件端点 SQL；列表投影故意省略 ProtectedValue。</summary>
internal static class NotificationRecipientEndpointSql
{
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
}
