using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy;

internal sealed class TenancyAuthorizationContributor
    : IAuthorizationCatalogContributor
{
    public AuthorizationModuleDefinition Module { get; } =
        new("tenancy", "租户管理", 20);

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
            TenancyTenantManagementPermissions.Create,
            "开通 Host 租户",
            AuthorizationScope.Host),
        new PermissionDefinition(
            TenancyTenantManagementPermissions.Update,
            "更新 Host 租户",
            AuthorizationScope.Host),
        new PermissionDefinition(
            TenancyTenantManagementPermissions.Disable,
            "禁用 Host 租户",
            AuthorizationScope.Host),
        new PermissionDefinition(
            TenancyTenantManagementPermissions.AssignPackage,
            "分配 Host 租户套餐",
            AuthorizationScope.Host),
        new PermissionDefinition(
            TenancyTenantPackagePermissions.Read,
            "查询租户套餐",
            AuthorizationScope.Host),
        new PermissionDefinition(
            TenancyTenantPackagePermissions.Create,
            "创建 Host 租户套餐",
            AuthorizationScope.Host),
        new PermissionDefinition(
            TenancyTenantPackagePermissions.Update,
            "更新 Host 租户套餐",
            AuthorizationScope.Host),
        new PermissionDefinition(
            TenancyTenantPackagePermissions.Disable,
            "禁用 Host 租户套餐",
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

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "tenancy.tenants.switch",
            "tenant-context",
            TenantsSwitch,
            "切换租户",
            "switch",
            10),
        new AuthorizationActionDefinition(
            "tenancy.tenants.create",
            "tenant-management",
            TenancyTenantManagementPermissions.Create,
            "开通租户",
            "create",
            10),
        new AuthorizationActionDefinition(
            "tenancy.tenants.update",
            "tenant-management",
            TenancyTenantManagementPermissions.Update,
            "编辑租户",
            "update",
            20),
        new AuthorizationActionDefinition(
            "tenancy.tenants.disable",
            "tenant-management",
            TenancyTenantManagementPermissions.Disable,
            "禁用租户",
            "disable",
            30),
        new AuthorizationActionDefinition(
            "tenancy.tenants.assign-package",
            "tenant-management",
            TenancyTenantManagementPermissions.AssignPackage,
            "分配套餐",
            "assign-package",
            40),
        new AuthorizationActionDefinition(
            "tenancy.tenant_packages.create",
            "tenant-packages",
            TenancyTenantPackagePermissions.Create,
            "创建套餐",
            "create",
            10),
        new AuthorizationActionDefinition(
            "tenancy.tenant_packages.update",
            "tenant-packages",
            TenancyTenantPackagePermissions.Update,
            "编辑套餐",
            "update",
            20),
        new AuthorizationActionDefinition(
            "tenancy.tenant_packages.disable",
            "tenant-packages",
            TenancyTenantPackagePermissions.Disable,
            "禁用套餐",
            "disable",
            30),
    ];
}
