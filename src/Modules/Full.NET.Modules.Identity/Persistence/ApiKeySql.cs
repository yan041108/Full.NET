using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>Host API Key 管理、认证与只读查询 SQL。</summary>
internal static class ApiKeySql
{
    public static readonly SqlStatement Insert = new(
        "identity.insert_api_key",
        """
        INSERT INTO fn_identity_api_key
            (Id, UserId, DisplayName, KeyPrefix, KeyHash, PermissionsJson,
             ExpiresAtUtc, IsActive, LastUsedAtUtc, DisabledAtUtc,
             CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @UserId, @DisplayName, @KeyPrefix, @KeyHash, @PermissionsJson,
             @ExpiresAtUtc, @IsActive, @LastUsedAtUtc, @DisabledAtUtc,
             @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "identity.find_api_key_by_id",
        """
        SELECT apiKey.Id,
               apiKey.UserId,
               identityUser.Username,
               apiKey.DisplayName,
               apiKey.KeyPrefix,
               apiKey.PermissionsJson,
               apiKey.ExpiresAtUtc,
               apiKey.IsActive,
               apiKey.LastUsedAtUtc,
               apiKey.CreatedAtUtc
        FROM fn_identity_api_key AS apiKey
        INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = apiKey.UserId
        WHERE apiKey.Id = @ApiKeyId
          AND identityUser.ScopeKey = 'host'
          AND identityUser.TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindForAuthentication = new(
        "identity.find_api_key_for_authentication",
        """
        SELECT apiKey.Id AS ApiKeyId,
               apiKey.UserId,
               identityUser.Username,
               identityUser.DisplayName,
               apiKey.PermissionsJson,
               apiKey.ExpiresAtUtc,
               apiKey.IsActive,
               identityUser.SecurityStamp,
               identityUser.IsActive AS UserIsActive,
               identityUser.LockoutEndUtc AS UserLockoutEndUtc
        FROM fn_identity_api_key AS apiKey
        INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = apiKey.UserId
        WHERE apiKey.KeyHash = @KeyHash
          AND identityUser.ScopeKey = 'host'
          AND identityUser.TenantId IS NULL
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement TouchLastUsed = new(
        "identity.touch_api_key_last_used",
        """
        UPDATE fn_identity_api_key
        SET LastUsedAtUtc = @LastUsedAtUtc,
            UpdatedAtUtc = @LastUsedAtUtc
        WHERE Id = @ApiKeyId
          AND IsActive = 1
          AND EXISTS (
              SELECT 1
              FROM fn_identity_user AS identityUser
              WHERE identityUser.Id = fn_identity_api_key.UserId
                AND identityUser.ScopeKey = 'host'
                AND identityUser.TenantId IS NULL
          )
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement Disable = new(
        "identity.disable_api_key",
        """
        UPDATE fn_identity_api_key
        SET IsActive = 0,
            DisabledAtUtc = @DisabledAtUtc,
            UpdatedAtUtc = @DisabledAtUtc,
            Version = Version + 1
        WHERE Id = @ApiKeyId
          AND IsActive = 1
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountHostApiKeysSqlServer = new(
        "identity.count_host_api_keys.sql_server",
        """
        SELECT COUNT(1)
        FROM fn_identity_api_key AS apiKey
        INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = apiKey.UserId
        WHERE identityUser.ScopeKey = 'host'
          AND identityUser.TenantId IS NULL
          AND (@UserId IS NULL OR apiKey.UserId = @UserId)
          AND (@DisplayNameContains IS NULL OR apiKey.DisplayName LIKE '%' + @DisplayNameContains + '%')
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountHostApiKeysMySql = new(
        "identity.count_host_api_keys.mysql",
        """
        SELECT COUNT(1)
        FROM fn_identity_api_key AS apiKey
        INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = apiKey.UserId
        WHERE identityUser.ScopeKey = 'host'
          AND identityUser.TenantId IS NULL
          AND (@UserId IS NULL OR apiKey.UserId = @UserId)
          AND (@DisplayNameContains IS NULL OR apiKey.DisplayName LIKE CONCAT('%', @DisplayNameContains, '%'))
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListHostApiKeysSqlServer = new(
        "identity.list_host_api_keys.sql_server",
        """
        SELECT apiKey.Id,
               apiKey.UserId,
               identityUser.Username,
               apiKey.DisplayName,
               apiKey.KeyPrefix,
               apiKey.PermissionsJson,
               apiKey.ExpiresAtUtc,
               apiKey.IsActive,
               apiKey.LastUsedAtUtc,
               apiKey.CreatedAtUtc
        FROM fn_identity_api_key AS apiKey
        INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = apiKey.UserId
        WHERE identityUser.ScopeKey = 'host'
          AND identityUser.TenantId IS NULL
          AND (@UserId IS NULL OR apiKey.UserId = @UserId)
          AND (@DisplayNameContains IS NULL OR apiKey.DisplayName LIKE '%' + @DisplayNameContains + '%')
        ORDER BY apiKey.CreatedAtUtc DESC, apiKey.Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListHostApiKeysMySql = new(
        "identity.list_host_api_keys.mysql",
        """
        SELECT apiKey.Id,
               apiKey.UserId,
               identityUser.Username,
               apiKey.DisplayName,
               apiKey.KeyPrefix,
               apiKey.PermissionsJson,
               apiKey.ExpiresAtUtc,
               apiKey.IsActive,
               apiKey.LastUsedAtUtc,
               apiKey.CreatedAtUtc
        FROM fn_identity_api_key AS apiKey
        INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = apiKey.UserId
        WHERE identityUser.ScopeKey = 'host'
          AND identityUser.TenantId IS NULL
          AND (@UserId IS NULL OR apiKey.UserId = @UserId)
          AND (@DisplayNameContains IS NULL OR apiKey.DisplayName LIKE CONCAT('%', @DisplayNameContains, '%'))
        ORDER BY apiKey.CreatedAtUtc DESC, apiKey.Id DESC
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);
}
