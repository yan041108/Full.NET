using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>OAuth 提供程序配置管理 SQL。</summary>
internal static class OAuthProviderSql
{
    private const string SelectColumns = """
        provider.Id,
               provider.ProviderKey,
               provider.DisplayName,
               provider.Authority,
               provider.ClientId,
               provider.ClientSecretProtected,
               provider.Scopes,
               provider.RedirectPath,
               provider.IsEnabled,
               provider.CreatedAtUtc,
               provider.UpdatedAtUtc,
               provider.Version
        """;

    public static readonly SqlStatement Insert = new(
        "identity.insert_oauth_provider",
        """
        INSERT INTO fn_identity_oauth_provider
            (Id, ProviderKey, DisplayName, Authority, ClientId, ClientSecretProtected,
             Scopes, RedirectPath, IsEnabled, CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @ProviderKey, @DisplayName, @Authority, @ClientId, @ClientSecretProtected,
             @Scopes, @RedirectPath, @IsEnabled, @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "identity.find_oauth_provider_by_id",
        $"""
        SELECT {SelectColumns}
        FROM fn_identity_oauth_provider AS provider
        WHERE provider.Id = @ProviderId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindByProviderKey = new(
        "identity.find_oauth_provider_by_provider_key",
        $"""
        SELECT {SelectColumns}
        FROM fn_identity_oauth_provider AS provider
        WHERE provider.ProviderKey = @ProviderKey
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Update = new(
        "identity.update_oauth_provider",
        """
        UPDATE fn_identity_oauth_provider
        SET DisplayName = @DisplayName,
            Authority = @Authority,
            ClientId = @ClientId,
            ClientSecretProtected = @ClientSecretProtected,
            Scopes = @Scopes,
            RedirectPath = @RedirectPath,
            IsEnabled = @IsEnabled,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @ProviderId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Delete = new(
        "identity.delete_oauth_provider",
        """
        DELETE FROM fn_identity_oauth_provider
        WHERE Id = @ProviderId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountSqlServer = new(
        "identity.count_oauth_providers.sql_server",
        """
        SELECT COUNT(1)
        FROM fn_identity_oauth_provider
        WHERE (@ProviderKeyContains IS NULL OR ProviderKey LIKE '%' + @ProviderKeyContains + '%')
          AND (@DisplayNameContains IS NULL OR DisplayName LIKE '%' + @DisplayNameContains + '%')
          AND (@IsEnabled IS NULL OR IsEnabled = @IsEnabled)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountMySql = new(
        "identity.count_oauth_providers.mysql",
        """
        SELECT COUNT(1)
        FROM fn_identity_oauth_provider
        WHERE (@ProviderKeyContains IS NULL OR ProviderKey LIKE CONCAT('%', @ProviderKeyContains, '%'))
          AND (@DisplayNameContains IS NULL OR DisplayName LIKE CONCAT('%', @DisplayNameContains, '%'))
          AND (@IsEnabled IS NULL OR IsEnabled = @IsEnabled)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListSqlServer = new(
        "identity.list_oauth_providers.sql_server",
        $"""
        SELECT {SelectColumns}
        FROM fn_identity_oauth_provider AS provider
        WHERE (@ProviderKeyContains IS NULL OR provider.ProviderKey LIKE '%' + @ProviderKeyContains + '%')
          AND (@DisplayNameContains IS NULL OR provider.DisplayName LIKE '%' + @DisplayNameContains + '%')
          AND (@IsEnabled IS NULL OR provider.IsEnabled = @IsEnabled)
        ORDER BY provider.DisplayName, provider.ProviderKey
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListMySql = new(
        "identity.list_oauth_providers.mysql",
        $"""
        SELECT {SelectColumns}
        FROM fn_identity_oauth_provider AS provider
        WHERE (@ProviderKeyContains IS NULL OR provider.ProviderKey LIKE CONCAT('%', @ProviderKeyContains, '%'))
          AND (@DisplayNameContains IS NULL OR provider.DisplayName LIKE CONCAT('%', @DisplayNameContains, '%'))
          AND (@IsEnabled IS NULL OR provider.IsEnabled = @IsEnabled)
        ORDER BY provider.DisplayName, provider.ProviderKey
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListEnabledPublic = new(
        "identity.list_enabled_oauth_providers_public",
        """
        SELECT provider.ProviderKey,
               provider.DisplayName
        FROM fn_identity_oauth_provider AS provider
        WHERE provider.IsEnabled = 1
        ORDER BY provider.DisplayName, provider.ProviderKey
        """,
        SqlDataScope.HostOnly);
}

/// <summary>公开 OAuth 提供程序列表查询投影。</summary>
internal sealed class OAuthPublicProviderRecord
{
    public string ProviderKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}
