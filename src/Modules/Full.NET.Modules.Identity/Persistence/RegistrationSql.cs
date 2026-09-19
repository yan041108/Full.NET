using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>注册策略与注册方式管理 SQL。</summary>
internal static class RegistrationPolicySql
{
    public static readonly SqlStatement GetPolicy = new(
        "identity.get_registration_policy",
        """
        SELECT Id, IsPublicRegistrationEnabled, RegistrationMode, UpdatedAtUtc, Version
        FROM fn_identity_registration_policy
        WHERE Id = @PolicyId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement UpdatePolicy = new(
        "identity.update_registration_policy",
        """
        UPDATE fn_identity_registration_policy
        SET IsPublicRegistrationEnabled = @IsPublicRegistrationEnabled,
            RegistrationMode = @RegistrationMode,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @PolicyId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);
}

/// <summary>注册方式管理 SQL。</summary>
internal static class RegistrationWaySql
{
    private const string SelectColumns = """
        way.Id,
               way.TenantId,
               way.Name,
               way.Code,
               way.IsEnabled,
               way.RoleId,
               way.OrganizationUnitId,
               way.PositionId,
               way.SortOrder,
               way.Remark,
               way.CreatedAtUtc,
               way.UpdatedAtUtc,
               way.Version
        """;

    public static readonly SqlStatement Insert = new(
        "identity.insert_registration_way",
        """
        INSERT INTO fn_identity_user_registration_way
            (Id, TenantId, Name, Code, IsEnabled, RoleId, OrganizationUnitId,
             PositionId, SortOrder, Remark, CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @Name, @Code, @IsEnabled, @RoleId, @OrganizationUnitId,
             @PositionId, @SortOrder, @Remark, @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "identity.find_registration_way_by_id",
        $"""
        SELECT {SelectColumns}
        FROM fn_identity_user_registration_way AS way
        WHERE way.Id = @WayId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindByTenantAndCode = new(
        "identity.find_registration_way_by_tenant_and_code",
        $"""
        SELECT {SelectColumns}
        FROM fn_identity_user_registration_way AS way
        WHERE way.TenantId = @TenantId
          AND way.Code = @Code
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement Update = new(
        "identity.update_registration_way",
        """
        UPDATE fn_identity_user_registration_way
        SET Name = @Name,
            Code = @Code,
            IsEnabled = @IsEnabled,
            RoleId = @RoleId,
            OrganizationUnitId = @OrganizationUnitId,
            PositionId = @PositionId,
            SortOrder = @SortOrder,
            Remark = @Remark,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @WayId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Delete = new(
        "identity.delete_registration_way",
        """
        DELETE FROM fn_identity_user_registration_way
        WHERE Id = @WayId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountSqlServer = new(
        "identity.count_registration_ways.sql_server",
        """
        SELECT COUNT(1)
        FROM fn_identity_user_registration_way
        WHERE (@TenantId IS NULL OR TenantId = @TenantId)
          AND (@NameContains IS NULL OR Name LIKE '%' + @NameContains + '%')
          AND (@IsEnabled IS NULL OR IsEnabled = @IsEnabled)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountMySql = new(
        "identity.count_registration_ways.mysql",
        """
        SELECT COUNT(1)
        FROM fn_identity_user_registration_way
        WHERE (@TenantId IS NULL OR TenantId = @TenantId)
          AND (@NameContains IS NULL OR Name LIKE CONCAT('%', @NameContains, '%'))
          AND (@IsEnabled IS NULL OR IsEnabled = @IsEnabled)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListSqlServer = new(
        "identity.list_registration_ways.sql_server",
        $"""
        SELECT {SelectColumns}
        FROM fn_identity_user_registration_way AS way
        WHERE (@TenantId IS NULL OR way.TenantId = @TenantId)
          AND (@NameContains IS NULL OR way.Name LIKE '%' + @NameContains + '%')
          AND (@IsEnabled IS NULL OR way.IsEnabled = @IsEnabled)
        ORDER BY way.SortOrder, way.Name, way.Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListMySql = new(
        "identity.list_registration_ways.mysql",
        $"""
        SELECT {SelectColumns}
        FROM fn_identity_user_registration_way AS way
        WHERE (@TenantId IS NULL OR way.TenantId = @TenantId)
          AND (@NameContains IS NULL OR way.Name LIKE CONCAT('%', @NameContains, '%'))
          AND (@IsEnabled IS NULL OR way.IsEnabled = @IsEnabled)
        ORDER BY way.SortOrder, way.Name, way.Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListEnabledByTenant = new(
        "identity.list_enabled_registration_ways_by_tenant",
        $"""
        SELECT {SelectColumns}
        FROM fn_identity_user_registration_way AS way
        WHERE way.TenantId = @TenantId
          AND way.IsEnabled = 1
        ORDER BY way.SortOrder, way.Name, way.Id
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindActiveTenantRole = new(
        "identity.find_active_tenant_role_for_registration_way",
        """
        SELECT Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
               IsSuperAdministrator, DataScopeKind,
               CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_identity_role
        WHERE Id = @RoleId
          AND TenantId = @TenantId
          AND IsActive = 1
        """,
        SqlDataScope.Global);
}
