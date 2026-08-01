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
        new PermissionDefinition(
            IdentityRoleManagementPermissions.Read,
            "查看 Host 角色",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityRoleManagementPermissions.Write,
            "管理 Host 角色",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityUserManagementPermissions.Export,
            "导出 Host 用户",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityRoleFieldGrantPermissions.Read,
            "读取角色字段授权",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityRoleFieldGrantPermissions.Write,
            "管理角色字段授权",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityMenuManagementPermissions.Read,
            "查看 Host 菜单",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityMenuManagementPermissions.Write,
            "管理 Host 菜单",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentitySessionManagementPermissions.Read,
            "查看 Host 在线会话",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentitySessionManagementPermissions.Write,
            "强制下线 Host 在线会话",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityApiKeyManagementPermissions.Read,
            "查看 Host API Key",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityApiKeyManagementPermissions.Write,
            "管理 Host API Key",
            AuthorizationScope.Host),
        new PermissionDefinition(
            ModuleCatalogPermissions.Read,
            "查看官方模块清单",
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
            "online-sessions",
            null,
            "online-sessions",
            "/identity/online-sessions",
            "online-sessions",
            "在线用户",
            "Online Sessions",
            "monitor",
            35,
            IdentitySessionManagementPermissions.Read),
        new NavigationDefinition(
            "api-keys",
            null,
            "api-keys",
            "/identity/api-keys",
            "api-keys",
            "API Key",
            "API Keys",
            "key",
            36,
            IdentityApiKeyManagementPermissions.Read),
        new NavigationDefinition(
            "modules",
            null,
            "modules",
            "/identity/modules",
            "modules",
            "模块清单",
            "Modules",
            "appstore",
            38,
            ModuleCatalogPermissions.Read),
        new NavigationDefinition(
            "roles",
            null,
            "roles",
            "/identity/roles",
            "roles",
            "角色管理",
            "Roles",
            "team",
            36,
            IdentityRoleManagementPermissions.Read),
        new NavigationDefinition(
            "menus",
            null,
            "menus",
            "/identity/menus",
            "menus",
            "菜单管理",
            "Menus",
            "menu",
            37,
            IdentityMenuManagementPermissions.Read),
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
