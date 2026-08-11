using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity;

internal sealed class IdentityAuthorizationContributor
    : IAuthorizationCatalogContributor
{
    public AuthorizationModuleDefinition Module { get; } =
        new("identity", "身份与权限", 10);

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
            IdentityUserManagementPermissions.Create,
            "创建 Host 用户",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityUserManagementPermissions.Update,
            "更新 Host 用户",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityUserManagementPermissions.AssignRoles,
            "分配 Host 用户角色",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityUserManagementPermissions.ResetPassword,
            "重置 Host 用户密码",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityUserManagementPermissions.Disable,
            "禁用 Host 用户",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityUserManagementPermissions.Enable,
            "启用 Host 用户",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityRoleManagementPermissions.Read,
            "查看 Host 角色",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityRoleManagementPermissions.Create,
            "创建 Host 角色",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityRoleManagementPermissions.Update,
            "更新 Host 角色",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityRoleManagementPermissions.AssignPermissions,
            "分配 Host 角色权限",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityRoleManagementPermissions.Disable,
            "禁用 Host 角色",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityRoleManagementPermissions.AssignDataScope,
            "配置 Host 角色数据范围",
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
            IdentityRoleFieldGrantPermissions.Replace,
            "替换角色字段授权",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityMenuManagementPermissions.Read,
            "查看 Host 菜单",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityMenuManagementPermissions.Create,
            "创建 Host 菜单",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityMenuManagementPermissions.Update,
            "更新 Host 菜单",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityMenuManagementPermissions.Disable,
            "禁用 Host 菜单",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentitySessionManagementPermissions.Read,
            "查看 Host 在线会话",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentitySessionManagementPermissions.Revoke,
            "强制下线 Host 在线会话",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityApiKeyManagementPermissions.Read,
            "查看 Host API Key",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityApiKeyManagementPermissions.Create,
            "创建 Host API Key",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityApiKeyManagementPermissions.Disable,
            "禁用 Host API Key",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityApiKeyManagementPermissions.Rotate,
            "轮换 Host API Key",
            AuthorizationScope.Host),
        new PermissionDefinition(
            ModuleCatalogPermissions.Read,
            "查看官方模块清单",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityOrganizationUnitProjectionPermissions.ReconcileDryRun,
            "机构投影对账 dry-run",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityOrganizationUnitProjectionPermissions.ReconcileApply,
            "机构投影对账 apply",
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
            DashboardRead,
            IsAffix: true),
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

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "identity.navigation.read",
            "overview",
            NavigationRead,
            "读取导航",
            "read-navigation",
            5),
        new AuthorizationActionDefinition(
            "identity.users.create",
            "users",
            IdentityUserManagementPermissions.Create,
            "创建用户",
            "create",
            10),
        new AuthorizationActionDefinition(
            "identity.users.update",
            "users",
            IdentityUserManagementPermissions.Update,
            "更新用户",
            "update",
            20),
        new AuthorizationActionDefinition(
            "identity.users.assign-roles",
            "users",
            IdentityUserManagementPermissions.AssignRoles,
            "分配角色",
            "assign-roles",
            30),
        new AuthorizationActionDefinition(
            "identity.users.reset-password",
            "users",
            IdentityUserManagementPermissions.ResetPassword,
            "重置密码",
            "reset-password",
            50),
        new AuthorizationActionDefinition(
            "identity.users.disable",
            "users",
            IdentityUserManagementPermissions.Disable,
            "禁用用户",
            "disable",
            60),
        new AuthorizationActionDefinition(
            "identity.users.enable",
            "users",
            IdentityUserManagementPermissions.Enable,
            "启用用户",
            "enable",
            70),
        new AuthorizationActionDefinition(
            "identity.users.export",
            "users",
            IdentityUserManagementPermissions.Export,
            "导出用户",
            "export",
            80),
        new AuthorizationActionDefinition(
            "identity.roles.create",
            "roles",
            IdentityRoleManagementPermissions.Create,
            "创建角色",
            "create",
            10),
        new AuthorizationActionDefinition(
            "identity.roles.update",
            "roles",
            IdentityRoleManagementPermissions.Update,
            "编辑角色",
            "update",
            20),
        new AuthorizationActionDefinition(
            "identity.roles.assign-permissions",
            "roles",
            IdentityRoleManagementPermissions.AssignPermissions,
            "分配权限",
            "assign-permissions",
            30),
        new AuthorizationActionDefinition(
            "identity.roles.disable",
            "roles",
            IdentityRoleManagementPermissions.Disable,
            "禁用角色",
            "disable",
            40),
        new AuthorizationActionDefinition(
            "identity.roles.assign-data-scope",
            "roles",
            IdentityRoleManagementPermissions.AssignDataScope,
            "数据范围",
            "assign-data-scope",
            50),
        new AuthorizationActionDefinition(
            "identity.role_field_grants.replace",
            "roles",
            IdentityRoleFieldGrantPermissions.Replace,
            "字段授权",
            "replace-field-grants",
            60),
        new AuthorizationActionDefinition(
            "identity.menus.create",
            "menus",
            IdentityMenuManagementPermissions.Create,
            "创建菜单",
            "create",
            10),
        new AuthorizationActionDefinition(
            "identity.menus.update",
            "menus",
            IdentityMenuManagementPermissions.Update,
            "编辑菜单",
            "update",
            20),
        new AuthorizationActionDefinition(
            "identity.menus.disable",
            "menus",
            IdentityMenuManagementPermissions.Disable,
            "禁用菜单",
            "disable",
            30),
        new AuthorizationActionDefinition(
            "identity.sessions.revoke",
            "online-sessions",
            IdentitySessionManagementPermissions.Revoke,
            "强制下线",
            "revoke",
            10),
        new AuthorizationActionDefinition(
            "identity.api_keys.create",
            "api-keys",
            IdentityApiKeyManagementPermissions.Create,
            "创建 API Key",
            "create",
            10),
        new AuthorizationActionDefinition(
            "identity.api_keys.disable",
            "api-keys",
            IdentityApiKeyManagementPermissions.Disable,
            "禁用 API Key",
            "disable",
            20),
        new AuthorizationActionDefinition(
            "identity.api_keys.rotate",
            "api-keys",
            IdentityApiKeyManagementPermissions.Rotate,
            "轮换 API Key",
            "rotate",
            30),
    ];
}
