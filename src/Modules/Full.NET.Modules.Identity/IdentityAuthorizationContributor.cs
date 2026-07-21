using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity;

internal sealed class IdentityAuthorizationContributor
    : IAuthorizationCatalogContributor
{
    internal const string DashboardRead = "platform.dashboard.read";
    internal const string NavigationRead = "identity.navigation.read";
    internal const string SuperAdministratorsRead =
        "identity.super_administrators.read";
    internal const string SuperAdministratorsManage =
        "identity.super_administrators.manage";

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
        new PermissionDefinition(
            SuperAdministratorsRead,
            "查看超级管理员",
            AuthorizationScope.Host),
        new PermissionDefinition(
            SuperAdministratorsManage,
            "管理超级管理员",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityUserManagementPermissions.Read,
            "查看 Host 用户",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityUserManagementPermissions.Write,
            "管理 Host 用户",
            AuthorizationScope.Host),
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
        new NavigationDefinition(
            "users",
            null,
            "users",
            "/identity/users",
            "users",
            "用户管理",
            "Users",
            "user",
            35,
            IdentityUserManagementPermissions.Read),
        new NavigationDefinition(
            "super-administrators",
            null,
            "super-administrators",
            "/identity/super-administrators",
            "super-administrators",
            "超级管理员",
            "Super Administrators",
            "shield",
            40,
            SuperAdministratorsRead),
    ];
}
