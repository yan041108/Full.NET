using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>OpenAccess 接入方应用管理 SQL。</summary>
internal static class OpenAccessClientSql
{
    private const string DetailSelectColumns = """
        client.Id,
               client.ApiKeyId,
               client.Name,
               client.Description,
               client.Remark,
               apiKey.UserId,
               identityUser.Username,
               apiKey.KeyPrefix,
               apiKey.PermissionsJson,
               apiKey.ExpiresAtUtc,
               apiKey.IsActive,
               apiKey.LastUsedAtUtc,
               client.CreatedAtUtc,
               client.Version,
               client.DailyRequestQuota
        """;

    private const string DetailJoinClause = """
        FROM fn_identity_open_access_client AS client
        INNER JOIN fn_identity_api_key AS apiKey ON apiKey.Id = client.ApiKeyId
        INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = apiKey.UserId
        """;

    public static readonly SqlStatement Insert = new(
        "identity.insert_open_access_client",
        """
        INSERT INTO fn_identity_open_access_client
            (Id, ApiKeyId, Name, Description, Remark, CreatedByUserId,
             CreatedAtUtc, UpdatedAtUtc, Version, DailyRequestQuota)
        VALUES
            (@Id, @ApiKeyId, @Name, @Description, @Remark, @CreatedByUserId,
             @CreatedAtUtc, @UpdatedAtUtc, @Version, @DailyRequestQuota)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "identity.find_open_access_client_by_id",
        $"""
        SELECT {DetailSelectColumns}
        {DetailJoinClause}
        WHERE client.Id = @ClientId
          AND identityUser.ScopeKey = 'host'
          AND identityUser.TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateMetadata = new(
        "identity.update_open_access_client_metadata",
        """
        UPDATE fn_identity_open_access_client
        SET Name = @Name,
            Description = @Description,
            Remark = @Remark,
            DailyRequestQuota = @DailyRequestQuota,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @ClientId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateApiKeyLink = new(
        "identity.update_open_access_client_api_key_link",
        """
        UPDATE fn_identity_open_access_client
        SET ApiKeyId = @ApiKeyId,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @ClientId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateApiKeyPermissions = new(
        "identity.update_open_access_client_api_key_permissions",
        """
        UPDATE fn_identity_api_key
        SET DisplayName = @DisplayName,
            PermissionsJson = @PermissionsJson,
            ExpiresAtUtc = @ExpiresAtUtc,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @ApiKeyId
          AND IsActive = 1
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountSqlServer = new(
        "identity.count_open_access_clients.sql_server",
        $"""
        SELECT COUNT(1)
        {DetailJoinClause}
        WHERE identityUser.ScopeKey = 'host'
          AND identityUser.TenantId IS NULL
          AND (@UserId IS NULL OR apiKey.UserId = @UserId)
          AND (@NameContains IS NULL OR client.Name LIKE '%' + @NameContains + '%')
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountMySql = new(
        "identity.count_open_access_clients.mysql",
        $"""
        SELECT COUNT(1)
        {DetailJoinClause}
        WHERE identityUser.ScopeKey = 'host'
          AND identityUser.TenantId IS NULL
          AND (@UserId IS NULL OR apiKey.UserId = @UserId)
          AND (@NameContains IS NULL OR client.Name LIKE CONCAT('%', @NameContains, '%'))
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListSqlServer = new(
        "identity.list_open_access_clients.sql_server",
        $"""
        SELECT {DetailSelectColumns}
        {DetailJoinClause}
        WHERE identityUser.ScopeKey = 'host'
          AND identityUser.TenantId IS NULL
          AND (@UserId IS NULL OR apiKey.UserId = @UserId)
          AND (@NameContains IS NULL OR client.Name LIKE '%' + @NameContains + '%')
        ORDER BY client.CreatedAtUtc DESC, client.Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListMySql = new(
        "identity.list_open_access_clients.mysql",
        $"""
        SELECT {DetailSelectColumns}
        {DetailJoinClause}
        WHERE identityUser.ScopeKey = 'host'
          AND identityUser.TenantId IS NULL
          AND (@UserId IS NULL OR apiKey.UserId = @UserId)
          AND (@NameContains IS NULL OR client.Name LIKE CONCAT('%', @NameContains, '%'))
        ORDER BY client.CreatedAtUtc DESC, client.Id DESC
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);
}
