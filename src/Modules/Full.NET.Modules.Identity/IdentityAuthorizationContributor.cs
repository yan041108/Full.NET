using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity;

internal sealed class IdentityAuthorizationContributor
    : IAuthorizationCatalogContributor
{
    internal const string DashboardRead = "platform.dashboard.read";
    internal const string NavigationRead = "identity.navigation.read";

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition(
            DashboardRead,
            "查看工作台",
            AuthorizationScope.Host | AuthorizationScope.Tenant),
        new PermissionDefinition(
            NavigationRead,
            "读取权限导航",
            AuthorizationScope.Host | AuthorizationScope.Tenant),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "overview",
            null,
            "overview",
            "/",
            "overview",
            "工作台",
            "Overview",
            "grid",
            10,
            DashboardRead),
    ];
}
