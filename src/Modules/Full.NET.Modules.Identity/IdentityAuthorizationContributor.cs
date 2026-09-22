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
    internal const string SuperAdministratorsGrant =
        "identity.super_administrators.grant";
    internal const string SuperAdministratorsRevoke =
        "identity.super_administrators.revoke";
    /// <summary>已退役粗粒度码；仅供迁移与 Architecture 退役清单引用。</summary>
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
            SuperAdministratorsGrant,
            "授予超级管理员",
            AuthorizationScope.Host),
        new PermissionDefinition(
            SuperAdministratorsRevoke,
            "撤销超级管理员",
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
            IdentityUserManagementPermissions.Retire,
            "退役 Host 用户",
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
            IdentityRoleManagementPermissions.Copy,
            "复制 Host 角色",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityRoleManagementPermissions.Enable,
            "启用 Host 角色",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityRoleManagementPermissions.Delete,
            "删除 Host 角色",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityRoleMemberManagementPermissions.ReplaceMembers,
            "管理 Host 角色成员",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityUserManagementPermissions.Export,
            "导出 Host 用户",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityUserManagementPermissions.Import,
            "导入 Host 用户",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityUserManagementPermissions.RevealPhoneNumber,
            "查看 Host 用户手机号明文",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityUserManagementPermissions.RevealIdCardNumber,
            "查看 Host 用户证件号明文",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityUserManagementPermissions.UnlockLogin,
            "解除 Host 用户登录锁定",
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
            IdentityOidcClientPermissions.Read,
            "查看 OIDC 客户端",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityOidcClientPermissions.Create,
            "创建 OIDC 客户端",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityOidcClientPermissions.Update,
            "更新 OIDC 客户端",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityOidcClientPermissions.Disable,
            "停用 OIDC 客户端",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityOidcClientPermissions.Rotate,
            "轮换 OIDC 客户端密钥",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityOidcAuthorizationPermissions.Read,
            "查看 OIDC 授权授予",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityOidcAuthorizationPermissions.Revoke,
            "撤销 OIDC 授权授予",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityOidcSigningKeyPermissions.Read,
            "查看 OIDC 签名密钥",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityOidcSigningKeyPermissions.Activate,
            "激活 OIDC 签名密钥",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityOpenAccessClientPermissions.Read,
            "查看 OpenAccess 接入方应用",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityOpenAccessClientPermissions.Create,
            "创建 OpenAccess 接入方应用",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityOpenAccessClientPermissions.Update,
            "更新 OpenAccess 接入方应用",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityOpenAccessClientPermissions.Disable,
            "停用 OpenAccess 接入方应用",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityOpenAccessClientPermissions.Rotate,
            "轮换 OpenAccess 接入方应用密钥",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityOpenAccessClientPermissions.DebugSignature,
            "调试 OpenAccess 接入方 HMAC 签名",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityRegistrationPolicyPermissions.Read,
            "查看注册策略",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityRegistrationPolicyPermissions.Update,
            "更新注册策略",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityRegistrationWayPermissions.Read,
            "查看注册方式",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityRegistrationWayPermissions.Create,
            "创建注册方式",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityRegistrationWayPermissions.Update,
            "更新注册方式",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityRegistrationWayPermissions.Delete,
            "删除注册方式",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityTenantMembershipPermissions.Read,
            "查看租户成员",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            IdentityTenantMembershipPermissions.Invite,
            "邀请租户成员",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            IdentityTenantMembershipPermissions.Update,
            "更新租户成员",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            IdentityTenantMembershipPermissions.Remove,
            "移除租户成员",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            IdentityTenantMembershipPermissions.RevokeInvitation,
            "撤销租户邀请",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            IdentityTenantMembershipPermissions.Provision,
            "创建租户成员",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            IdentityLdapConnectionPermissions.Read,
            "查看 LDAP 连接",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityLdapConnectionPermissions.Create,
            "创建 LDAP 连接",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityLdapConnectionPermissions.Update,
            "更新 LDAP 连接",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityLdapConnectionPermissions.Delete,
            "删除 LDAP 连接",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityLdapConnectionPermissions.Test,
            "测试 LDAP 连接",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityLdapConnectionPermissions.PreviewSync,
            "预览 LDAP 同步",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityOAuthProviderPermissions.Read,
            "查看 OAuth 提供程序",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityOAuthProviderPermissions.Create,
            "创建 OAuth 提供程序",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityOAuthProviderPermissions.Update,
            "更新 OAuth 提供程序",
            AuthorizationScope.Host),
        new PermissionDefinition(
            IdentityOAuthProviderPermissions.Delete,
            "删除 OAuth 提供程序",
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
            "open-access-clients",
            null,
            "open-access-clients",
            "/identity/open-access-clients",
            "open-access-clients",
            "接入方应用",
            "OpenAccess Clients",
            "connection",
            37,
            IdentityOpenAccessClientPermissions.Read),
        new NavigationDefinition(
            "oidc-clients",
            null,
            "oidc-clients",
            "/identity/oidc-clients",
            "oidc-clients",
            "OIDC 客户端",
            "OIDC Clients",
            "key",
            38,
            IdentityOidcClientPermissions.Read),
        new NavigationDefinition(
            "oidc-authorizations",
            null,
            "oidc-authorizations",
            "/identity/oidc-authorizations",
            "oidc-authorizations",
            "OIDC 授权授予",
            "OIDC Authorizations",
            "document",
            39,
            IdentityOidcAuthorizationPermissions.Read),
        new NavigationDefinition(
            "oidc-signing-keys",
            null,
            "oidc-signing-keys",
            "/identity/oidc-signing-keys",
            "oidc-signing-keys",
            "OIDC 签名密钥",
            "OIDC Signing Keys",
            "key",
            40,
            IdentityOidcSigningKeyPermissions.Read),
        new NavigationDefinition(
            "registration-ways",
            null,
            "registration-ways",
            "/identity/registration-ways",
            "registration-ways",
            "注册方式",
            "Registration Ways",
            "user-add",
            38,
            IdentityRegistrationWayPermissions.Read),
        new NavigationDefinition(
            "tenant-members",
            null,
            "tenant-members",
            "/identity/tenant-members",
            "tenant-members",
            "租户成员",
            "Tenant Members",
            "peoples",
            39,
            IdentityTenantMembershipPermissions.Read),
        new NavigationDefinition(
            "ldap-connections",
            null,
            "ldap-connections",
            "/identity/ldap-connections",
            "ldap-connections",
            "LDAP 连接",
            "LDAP Connections",
            "link",
            39,
            IdentityLdapConnectionPermissions.Read),
        new NavigationDefinition(
            "oauth-providers",
            null,
            "oauth-providers",
            "/identity/oauth-providers",
            "oauth-providers",
            "OAuth 提供程序",
            "OAuth Providers",
            "connection",
            40,
            IdentityOAuthProviderPermissions.Read),
        new NavigationDefinition(
            "module-selection",
            null,
            "module-selection",
            "/identity/module-selection",
            "module-selection",
            "模块启用预览",
            "Module Selection",
            "setting",
            37,
            ModuleCatalogPermissions.Read),
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
            "identity.users.retire",
            "users",
            IdentityUserManagementPermissions.Retire,
            "退役用户",
            "retire",
            72),
        new AuthorizationActionDefinition(
            "identity.users.unlock-login",
            "users",
            IdentityUserManagementPermissions.UnlockLogin,
            "解除登录锁定",
            "unlock-login",
            75),
        new AuthorizationActionDefinition(
            "identity.users.export",
            "users",
            IdentityUserManagementPermissions.Export,
            "导出用户",
            "export",
            80),
        new AuthorizationActionDefinition(
            "identity.users.import",
            "users",
            IdentityUserManagementPermissions.Import,
            "导入用户",
            "import",
            90),
        new AuthorizationActionDefinition(
            "identity.users.reveal-phone-number",
            "users",
            IdentityUserManagementPermissions.RevealPhoneNumber,
            "查看手机号明文",
            "reveal-phone-number",
            95),
        new AuthorizationActionDefinition(
            "identity.users.reveal-id-card-number",
            "users",
            IdentityUserManagementPermissions.RevealIdCardNumber,
            "查看证件号明文",
            "reveal-id-card-number",
            96),
        new AuthorizationActionDefinition(
            "identity.roles.create",
            "roles",
            IdentityRoleManagementPermissions.Create,
            "创建角色",
            "create",
            10),
        new AuthorizationActionDefinition(
            "identity.roles.copy",
            "roles",
            IdentityRoleManagementPermissions.Copy,
            "复制角色",
            "copy",
            15),
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
            "identity.roles.enable",
            "roles",
            IdentityRoleManagementPermissions.Enable,
            "启用角色",
            "enable",
            45),
        new AuthorizationActionDefinition(
            "identity.roles.delete",
            "roles",
            IdentityRoleManagementPermissions.Delete,
            "删除角色",
            "delete",
            50),
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
            "identity.roles.replace-members",
            "roles",
            IdentityRoleMemberManagementPermissions.ReplaceMembers,
            "成员管理",
            "replace-members",
            70),
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
        new AuthorizationActionDefinition(
            "identity.oidc_clients.create",
            "oidc-clients",
            IdentityOidcClientPermissions.Create,
            "创建 OIDC 客户端",
            "create",
            10),
        new AuthorizationActionDefinition(
            "identity.oidc_clients.update",
            "oidc-clients",
            IdentityOidcClientPermissions.Update,
            "编辑 OIDC 客户端",
            "update",
            20),
        new AuthorizationActionDefinition(
            "identity.oidc_clients.disable",
            "oidc-clients",
            IdentityOidcClientPermissions.Disable,
            "停用 OIDC 客户端",
            "disable",
            30),
        new AuthorizationActionDefinition(
            "identity.oidc_clients.rotate",
            "oidc-clients",
            IdentityOidcClientPermissions.Rotate,
            "轮换 OIDC 密钥",
            "rotate",
            40),
        new AuthorizationActionDefinition(
            "identity.oidc_authorizations.revoke",
            "oidc-authorizations",
            IdentityOidcAuthorizationPermissions.Revoke,
            "撤销 OIDC 授权",
            "revoke",
            10),
        new AuthorizationActionDefinition(
            "identity.open_access_clients.create",
            "open-access-clients",
            IdentityOpenAccessClientPermissions.Create,
            "创建接入方应用",
            "create",
            10),
        new AuthorizationActionDefinition(
            "identity.open_access_clients.update",
            "open-access-clients",
            IdentityOpenAccessClientPermissions.Update,
            "编辑接入方应用",
            "update",
            20),
        new AuthorizationActionDefinition(
            "identity.open_access_clients.disable",
            "open-access-clients",
            IdentityOpenAccessClientPermissions.Disable,
            "停用接入方应用",
            "disable",
            30),
        new AuthorizationActionDefinition(
            "identity.open_access_clients.rotate",
            "open-access-clients",
            IdentityOpenAccessClientPermissions.Rotate,
            "轮换接入方密钥",
            "rotate",
            40),
        new AuthorizationActionDefinition(
            "identity.open_access_clients.debug_signature",
            "open-access-clients",
            IdentityOpenAccessClientPermissions.DebugSignature,
            "签名调试",
            "debug-signature",
            50),
        new AuthorizationActionDefinition(
            "identity.registration_ways.create",
            "registration-ways",
            IdentityRegistrationWayPermissions.Create,
            "创建注册方式",
            "create",
            10),
        new AuthorizationActionDefinition(
            "identity.registration_ways.update",
            "registration-ways",
            IdentityRegistrationWayPermissions.Update,
            "编辑注册方式",
            "update",
            20),
        new AuthorizationActionDefinition(
            "identity.registration_ways.delete",
            "registration-ways",
            IdentityRegistrationWayPermissions.Delete,
            "删除注册方式",
            "delete",
            30),
        new AuthorizationActionDefinition(
            "identity.ldap_connections.create",
            "ldap-connections",
            IdentityLdapConnectionPermissions.Create,
            "创建 LDAP 连接",
            "create",
            10),
        new AuthorizationActionDefinition(
            "identity.ldap_connections.update",
            "ldap-connections",
            IdentityLdapConnectionPermissions.Update,
            "编辑 LDAP 连接",
            "update",
            20),
        new AuthorizationActionDefinition(
            "identity.ldap_connections.delete",
            "ldap-connections",
            IdentityLdapConnectionPermissions.Delete,
            "删除 LDAP 连接",
            "delete",
            30),
        new AuthorizationActionDefinition(
            "identity.ldap_connections.test",
            "ldap-connections",
            IdentityLdapConnectionPermissions.Test,
            "测试 LDAP 连接",
            "test",
            40),
        new AuthorizationActionDefinition(
            "identity.ldap_connections.preview_sync",
            "ldap-connections",
            IdentityLdapConnectionPermissions.PreviewSync,
            "预览 LDAP 同步",
            "preview-sync",
            50),
        new AuthorizationActionDefinition(
            "identity.oauth_providers.create",
            "oauth-providers",
            IdentityOAuthProviderPermissions.Create,
            "创建 OAuth 提供程序",
            "create",
            10),
        new AuthorizationActionDefinition(
            "identity.oauth_providers.update",
            "oauth-providers",
            IdentityOAuthProviderPermissions.Update,
            "编辑 OAuth 提供程序",
            "update",
            20),
        new AuthorizationActionDefinition(
            "identity.oauth_providers.delete",
            "oauth-providers",
            IdentityOAuthProviderPermissions.Delete,
            "删除 OAuth 提供程序",
            "delete",
            30),
    ];
}
