using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>LDAP 连接配置管理 SQL。</summary>
internal static class LdapConnectionSql
{
    private const string SelectColumns = """
        connection.Id,
               connection.TenantId,
               connection.Name,
               connection.Host,
               connection.Port,
               connection.UseTls,
               connection.BaseDn,
               connection.BindDn,
               connection.BindPasswordProtected,
               connection.UserSearchFilter,
               connection.UserAccountAttribute,
               connection.EmployeeIdAttribute,
               connection.DepartmentCodeAttribute,
               connection.SyncSearchBaseDn,
               connection.IsEnabled,
               connection.CreatedAtUtc,
               connection.UpdatedAtUtc,
               connection.Version
        """;

    public static readonly SqlStatement Insert = new(
        "identity.insert_ldap_connection",
        """
        INSERT INTO fn_identity_ldap_connection
            (Id, TenantId, Name, Host, Port, UseTls, BaseDn, BindDn, BindPasswordProtected,
             UserSearchFilter, UserAccountAttribute, EmployeeIdAttribute, DepartmentCodeAttribute,
             SyncSearchBaseDn, IsEnabled, CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @Name, @Host, @Port, @UseTls, @BaseDn, @BindDn, @BindPasswordProtected,
             @UserSearchFilter, @UserAccountAttribute, @EmployeeIdAttribute, @DepartmentCodeAttribute,
             @SyncSearchBaseDn, @IsEnabled, @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "identity.find_ldap_connection_by_id",
        $"""
        SELECT {SelectColumns}
        FROM fn_identity_ldap_connection AS connection
        WHERE connection.Id = @ConnectionId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindByTenantScope = new(
        "identity.find_ldap_connection_by_tenant_scope",
        $"""
        SELECT {SelectColumns}
        FROM fn_identity_ldap_connection AS connection
        WHERE ((@TenantId IS NULL AND connection.TenantId IS NULL)
            OR connection.TenantId = @TenantId)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Update = new(
        "identity.update_ldap_connection",
        """
        UPDATE fn_identity_ldap_connection
        SET Name = @Name,
            Host = @Host,
            Port = @Port,
            UseTls = @UseTls,
            BaseDn = @BaseDn,
            BindDn = @BindDn,
            BindPasswordProtected = @BindPasswordProtected,
            UserSearchFilter = @UserSearchFilter,
            UserAccountAttribute = @UserAccountAttribute,
            EmployeeIdAttribute = @EmployeeIdAttribute,
            DepartmentCodeAttribute = @DepartmentCodeAttribute,
            SyncSearchBaseDn = @SyncSearchBaseDn,
            IsEnabled = @IsEnabled,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @ConnectionId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Disable = new(
        "identity.disable_ldap_connection",
        """
        UPDATE fn_identity_ldap_connection
        SET IsEnabled = 0,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @ConnectionId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Delete = new(
        "identity.delete_ldap_connection",
        """
        DELETE FROM fn_identity_ldap_connection
        WHERE Id = @ConnectionId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountSqlServer = new(
        "identity.count_ldap_connections.sql_server",
        """
        SELECT COUNT(1)
        FROM fn_identity_ldap_connection
        WHERE (@TenantId IS NULL OR TenantId = @TenantId)
          AND (@NameContains IS NULL OR Name LIKE '%' + @NameContains + '%')
          AND (@IsEnabled IS NULL OR IsEnabled = @IsEnabled)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountMySql = new(
        "identity.count_ldap_connections.mysql",
        """
        SELECT COUNT(1)
        FROM fn_identity_ldap_connection
        WHERE (@TenantId IS NULL OR TenantId = @TenantId)
          AND (@NameContains IS NULL OR Name LIKE CONCAT('%', @NameContains, '%'))
          AND (@IsEnabled IS NULL OR IsEnabled = @IsEnabled)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListSqlServer = new(
        "identity.list_ldap_connections.sql_server",
        $"""
        SELECT {SelectColumns}
        FROM fn_identity_ldap_connection AS connection
        WHERE (@TenantId IS NULL OR connection.TenantId = @TenantId)
          AND (@NameContains IS NULL OR connection.Name LIKE '%' + @NameContains + '%')
          AND (@IsEnabled IS NULL OR connection.IsEnabled = @IsEnabled)
        ORDER BY connection.Name, connection.Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListMySql = new(
        "identity.list_ldap_connections.mysql",
        $"""
        SELECT {SelectColumns}
        FROM fn_identity_ldap_connection AS connection
        WHERE (@TenantId IS NULL OR connection.TenantId = @TenantId)
          AND (@NameContains IS NULL OR connection.Name LIKE CONCAT('%', @NameContains, '%'))
          AND (@IsEnabled IS NULL OR connection.IsEnabled = @IsEnabled)
        ORDER BY connection.Name, connection.Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);
}
