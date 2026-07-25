using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Tenancy.Persistence;

internal static class TenantSql
{
    public static readonly SqlStatement FindByIdentifier = new(
        "tenancy.find_by_identifier",
        """
        SELECT COUNT(*)
        FROM fn_tenancy_tenant
        WHERE Identifier = @Identifier
        """,
        SqlDataScope.Global);

    // Seeder 只在 Migrator 的可信宿主上下文使用该 Global 查询，并以自然键判断幂等状态。
    public static readonly SqlStatement FindSummaryByIdentifier = new(
        "tenancy.tenant.find_summary_by_identifier",
        """
        SELECT Id, Identifier, Name, Domain, IsActive, Version
        FROM fn_tenancy_tenant
        WHERE Identifier = @Identifier
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CountByDomain = new(
        "tenancy.count_by_domain",
        """
        SELECT COUNT(*)
        FROM fn_tenancy_tenant
        WHERE Domain = @Domain
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindByDomain = new(
        "tenancy.find_by_domain",
        """
        SELECT Id, Identifier, Name, Domain, IsActive, Version, DefaultLocale
        FROM fn_tenancy_tenant
        WHERE Domain = @Domain
        """,
        SqlDataScope.Global);

    // 按 ID 的 Global 查询只服务于宿主管理员上下文切换，调用方必须先通过权限策略。
    public static readonly SqlStatement FindById = new(
        "tenancy.find_by_explicit_id",
        """
        SELECT Id, Identifier, Name, Domain, IsActive, Version, DefaultLocale
        FROM fn_tenancy_tenant
        WHERE Id = @TenantId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement GetAvailable = new(
        "tenancy.get_available_for_host_administrator",
        """
        SELECT Id, Identifier, Name, Domain, IsActive, Version, DefaultLocale
        FROM fn_tenancy_tenant
        WHERE IsActive = 1
        ORDER BY Name, Identifier, Id
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement Insert = new(
        "tenancy.insert",
        """
        INSERT INTO fn_tenancy_tenant
            (Id, Identifier, Name, Domain, IsActive, CreatedAtUtc, Version, DefaultLocale, TenantPackageId)
        VALUES
            (@Id, @Identifier, @Name, @Domain, @IsActive, @CreatedAtUtc, @Version, @DefaultLocale, @TenantPackageId)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement GetCurrent = new(
        "tenancy.get_current",
        """
        SELECT Id, Identifier, Name, Domain, IsActive, Version, DefaultLocale
        FROM fn_tenancy_tenant
        WHERE Id = @TenantId AND IsActive = 1
        """,
        SqlDataScope.TenantRequired);

    public static readonly SqlStatement CountHostTenants = new(
        "tenancy.count_host_tenants",
        """
        SELECT COUNT(1)
        FROM fn_tenancy_tenant
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListHostTenantsSqlServer = new(
        "tenancy.list_host_tenants.sql_server",
        """
        SELECT tenant.Id,
               tenant.Identifier,
               tenant.Name,
               tenant.Domain,
               tenant.IsActive,
               tenant.Version,
               tenant.DefaultLocale,
               tenant.TenantPackageId,
               package.Code AS TenantPackageCode,
               package.Name AS TenantPackageName
        FROM fn_tenancy_tenant AS tenant
        LEFT JOIN fn_tenancy_tenant_package AS package
            ON package.Id = tenant.TenantPackageId
        ORDER BY tenant.Name, tenant.Identifier, tenant.Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListHostTenantsMySql = new(
        "tenancy.list_host_tenants.mysql",
        """
        SELECT tenant.Id,
               tenant.Identifier,
               tenant.Name,
               tenant.Domain,
               tenant.IsActive,
               tenant.Version,
               tenant.DefaultLocale,
               tenant.TenantPackageId,
               package.Code AS TenantPackageCode,
               package.Name AS TenantPackageName
        FROM fn_tenancy_tenant AS tenant
        LEFT JOIN fn_tenancy_tenant_package AS package
            ON package.Id = tenant.TenantPackageId
        ORDER BY tenant.Name, tenant.Identifier, tenant.Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindHostTenantById = new(
        "tenancy.find_host_tenant_by_id",
        """
        SELECT tenant.Id,
               tenant.Identifier,
               tenant.Name,
               tenant.Domain,
               tenant.IsActive,
               tenant.Version,
               tenant.DefaultLocale,
               tenant.TenantPackageId,
               package.Code AS TenantPackageCode,
               package.Name AS TenantPackageName
        FROM fn_tenancy_tenant AS tenant
        LEFT JOIN fn_tenancy_tenant_package AS package
            ON package.Id = tenant.TenantPackageId
        WHERE tenant.Id = @TenantId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement AssignHostTenantPackage = new(
        "tenancy.assign_host_tenant_package",
        """
        UPDATE fn_tenancy_tenant
        SET TenantPackageId = @TenantPackageId,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @TenantId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountActiveTenants = new(
        "tenancy.count_active_tenants",
        """
        SELECT COUNT(1)
        FROM fn_tenancy_tenant
        WHERE IsActive = 1
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateHostTenantName = new(
        "tenancy.update_host_tenant_name",
        """
        UPDATE fn_tenancy_tenant
        SET Name = @Name,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @TenantId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DisableHostTenant = new(
        "tenancy.disable_host_tenant",
        """
        UPDATE fn_tenancy_tenant
        SET IsActive = 0,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @TenantId
          AND IsActive = 1
        """,
        SqlDataScope.HostOnly);
}
