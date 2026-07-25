using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy;

internal sealed class TenancyAuthorizationContributor
    : IAuthorizationCatalogContributor
{
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition(
            TenancyTenantManagementPermissions.Read,
            "读取可用租户",
            AuthorizationScope.Host | AuthorizationScope.Tenant),
        new PermissionDefinition(
            TenancyTenantManagementPermissions.HostTenantsRead,
            "查询租户目录",
            AuthorizationScope.Host),
        new PermissionDefinition(
            TenancyTenantManagementPermissions.Write,
            "管理租户",
            AuthorizationScope.Host),
        new PermissionDefinition(
            TenancyTenantPackagePermissions.Read,
            "查询租户套餐",
            AuthorizationScope.Host),
        new PermissionDefinition(
            TenancyTenantPackagePermissions.Write,
            "管理租户套餐",
            AuthorizationScope.Host),
        new PermissionDefinition(
            TenantsSwitch,
            "切换租户上下文",
            AuthorizationScope.Host | AuthorizationScope.Tenant),
    ];

    internal const string TenantsRead = TenancyTenantManagementPermissions.Read;
    internal const string TenantsSwitch = "tenancy.tenants.switch";

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "tenant-context",
            null,
            "tenant-context",
            "/tenant-context",
            "tenant-context",
            "租户上下文",
            "Tenant Context",
            "office-building",
            20,
            TenancyTenantManagementPermissions.Read),
        new NavigationDefinition(
            "tenant-management",
            null,
            "tenant-management",
            "/tenants",
            "tenants",
            "租户管理",
            "Tenant Management",
            "grid",
            21,
            TenancyTenantManagementPermissions.HostTenantsRead),
        new NavigationDefinition(
            "tenant-packages",
            null,
            "tenant-packages",
            "/tenant-packages",
            "tenant-packages",
            "租户套餐",
            "Tenant Packages",
            "collection",
            22,
            TenancyTenantPackagePermissions.Read),
    ];
}
