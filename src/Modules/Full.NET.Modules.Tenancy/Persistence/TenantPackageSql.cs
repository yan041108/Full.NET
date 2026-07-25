using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Tenancy.Persistence;

internal static class TenantPackageSql
{
    public static readonly SqlStatement CountHostPackages = new(
        "tenancy.count_host_tenant_packages",
        """
        SELECT COUNT(1)
        FROM fn_tenancy_tenant_package
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListHostPackagesSqlServer = new(
        "tenancy.list_host_tenant_packages.sql_server",
        """
        SELECT package.Id,
               package.Code,
               package.Name,
               package.Description,
               package.IsActive,
               package.Version,
               CAST((
                   SELECT COUNT(1)
                   FROM fn_tenancy_tenant tenant
                   WHERE tenant.TenantPackageId = package.Id
               ) AS BIGINT) AS AssignedTenantCount
        FROM fn_tenancy_tenant_package package
        ORDER BY package.Name, package.Code, package.Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListHostPackagesMySql = new(
        "tenancy.list_host_tenant_packages.mysql",
        """
        SELECT package.Id,
               package.Code,
               package.Name,
               package.Description,
               package.IsActive,
               package.Version,
               (
                   SELECT COUNT(1)
                   FROM fn_tenancy_tenant tenant
                   WHERE tenant.TenantPackageId = package.Id
               ) AS AssignedTenantCount
        FROM fn_tenancy_tenant_package package
        ORDER BY package.Name, package.Code, package.Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindPackageById = new(
        "tenancy.tenant_package.find_package_by_id",
        """
        SELECT Id, Code, Name, Description, IsActive, Version
        FROM fn_tenancy_tenant_package
        WHERE Id = @PackageId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindByIdSqlServer = new(
        "tenancy.tenant_package.find_by_id.sql_server",
        """
        SELECT package.Id,
               package.Code,
               package.Name,
               package.Description,
               package.IsActive,
               package.Version,
               CAST((
                   SELECT COUNT(1)
                   FROM fn_tenancy_tenant tenant
                   WHERE tenant.TenantPackageId = package.Id
               ) AS BIGINT) AS AssignedTenantCount
        FROM fn_tenancy_tenant_package package
        WHERE package.Id = @PackageId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindByIdMySql = new(
        "tenancy.tenant_package.find_by_id.mysql",
        """
        SELECT package.Id,
               package.Code,
               package.Name,
               package.Description,
               package.IsActive,
               package.Version,
               (
                   SELECT COUNT(1)
                   FROM fn_tenancy_tenant tenant
                   WHERE tenant.TenantPackageId = package.Id
               ) AS AssignedTenantCount
        FROM fn_tenancy_tenant_package package
        WHERE package.Id = @PackageId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindByCode = new(
        "tenancy.tenant_package.find_by_code",
        """
        SELECT Id, Code, Name, Description, IsActive, Version
        FROM fn_tenancy_tenant_package
        WHERE Code = @Code
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement Insert = new(
        "tenancy.tenant_package.insert",
        """
        INSERT INTO fn_tenancy_tenant_package
            (Id, Code, Name, Description, IsActive, CreatedAtUtc, Version)
        VALUES
            (@Id, @Code, @Name, @Description, @IsActive, @CreatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateHostPackage = new(
        "tenancy.tenant_package.update",
        """
        UPDATE fn_tenancy_tenant_package
        SET Name = @Name,
            Description = @Description,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @PackageId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DisableHostPackage = new(
        "tenancy.tenant_package.disable",
        """
        UPDATE fn_tenancy_tenant_package
        SET IsActive = 0,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @PackageId
          AND IsActive = 1
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountAssignedTenants = new(
        "tenancy.tenant_package.count_assigned_tenants",
        """
        SELECT COUNT(1)
        FROM fn_tenancy_tenant
        WHERE TenantPackageId = @PackageId
        """,
        SqlDataScope.HostOnly);
}
