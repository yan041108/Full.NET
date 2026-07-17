using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Tenancy;

internal sealed class TenancyAuthorizationContributor
    : IAuthorizationCatalogContributor
{
    internal const string TenantsRead = "tenancy.tenants.read";
    internal const string TenantsSwitch = "tenancy.tenants.switch";

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition(
            TenantsRead,
            "读取可用租户",
            AuthorizationScope.Host),
        new PermissionDefinition(
            TenantsSwitch,
            "切换租户上下文",
            AuthorizationScope.Host),
    ];

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
            TenantsRead),
    ];
}
