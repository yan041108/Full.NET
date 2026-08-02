using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Settings.Contracts;

namespace Full.NET.Modules.Settings;

internal sealed class SettingsAuthorizationContributor
    : IAuthorizationCatalogContributor
{
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition(
            DictTypeManagementPermissions.Read,
            "查询数据字典",
            AuthorizationScope.Host),
        new PermissionDefinition(
            DictTypeManagementPermissions.Create,
            "创建数据字典",
            AuthorizationScope.Host),
        new PermissionDefinition(
            DictTypeManagementPermissions.Update,
            "更新数据字典",
            AuthorizationScope.Host),
        new PermissionDefinition(
            DictTypeManagementPermissions.Disable,
            "禁用数据字典",
            AuthorizationScope.Host),
        new PermissionDefinition(
            ConfigEntryManagementPermissions.Read,
            "查询系统配置",
            AuthorizationScope.Host),
        new PermissionDefinition(
            ConfigEntryManagementPermissions.Create,
            "创建系统配置",
            AuthorizationScope.Host),
        new PermissionDefinition(
            ConfigEntryManagementPermissions.Update,
            "更新系统配置",
            AuthorizationScope.Host),
        new PermissionDefinition(
            ConfigEntryManagementPermissions.Disable,
            "禁用系统配置",
            AuthorizationScope.Host),
        new PermissionDefinition(
            DiagnosticPolicyManagementPermissions.Read,
            "查询限时诊断策略",
            AuthorizationScope.Host),
        new PermissionDefinition(
            DiagnosticPolicyManagementPermissions.Write,
            "管理限时诊断策略",
            AuthorizationScope.Host),
        new PermissionDefinition(
            EnumCatalogPermissions.Read,
            "查询枚举与常量目录",
            AuthorizationScope.Host),
        new PermissionDefinition(
            TenantDictTypeManagementPermissions.Read,
            "查询租户数据字典",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            TenantDictTypeManagementPermissions.Create,
            "创建租户数据字典",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            TenantDictTypeManagementPermissions.Update,
            "更新租户数据字典",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            TenantDictTypeManagementPermissions.Disable,
            "禁用租户数据字典",
            AuthorizationScope.Tenant),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "dict-types",
            null,
            "dict-types",
            "/settings/dict-types",
            "dict-types",
            "数据字典",
            "Data Dictionary",
            "collection",
            50,
            DictTypeManagementPermissions.Read),
        new NavigationDefinition(
            "config-entries",
            null,
            "config-entries",
            "/settings/config-entries",
            "config-entries",
            "系统配置",
            "System Settings",
            "setting",
            51,
            ConfigEntryManagementPermissions.Read),
        new NavigationDefinition(
            "diagnostic-policy",
            null,
            "diagnostic-policy",
            "/settings/diagnostic-policy",
            "diagnostic-policy",
            "限时诊断",
            "Diagnostic Policy",
            "monitor",
            54,
            DiagnosticPolicyManagementPermissions.Read),
        new NavigationDefinition(
            "enum-catalogs",
            null,
            "enum-catalogs",
            "/settings/enum-catalogs",
            "enum-catalogs",
            "枚举常量",
            "Enum Catalogs",
            "list",
            52,
            EnumCatalogPermissions.Read),
        new NavigationDefinition(
            "tenant-dict-types",
            null,
            "tenant-dict-types",
            "/settings/tenant-dict-types",
            "tenant-dict-types",
            "租户数据字典",
            "Tenant Dictionaries",
            "collection",
            53,
            TenantDictTypeManagementPermissions.Read),
    ];

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "settings.dict_types.create",
            "dict-types",
            DictTypeManagementPermissions.Create,
            "创建字典",
            "create",
            10),
        new AuthorizationActionDefinition(
            "settings.dict_types.update",
            "dict-types",
            DictTypeManagementPermissions.Update,
            "编辑字典",
            "update",
            20),
        new AuthorizationActionDefinition(
            "settings.dict_types.disable",
            "dict-types",
            DictTypeManagementPermissions.Disable,
            "禁用字典",
            "disable",
            30),
        new AuthorizationActionDefinition(
            "settings.tenant_dict_types.create",
            "tenant-dict-types",
            TenantDictTypeManagementPermissions.Create,
            "创建租户字典",
            "create",
            10),
        new AuthorizationActionDefinition(
            "settings.tenant_dict_types.update",
            "tenant-dict-types",
            TenantDictTypeManagementPermissions.Update,
            "编辑租户字典",
            "update",
            20),
        new AuthorizationActionDefinition(
            "settings.tenant_dict_types.disable",
            "tenant-dict-types",
            TenantDictTypeManagementPermissions.Disable,
            "禁用租户字典",
            "disable",
            30),
        new AuthorizationActionDefinition(
            "settings.config.create",
            "config-entries",
            ConfigEntryManagementPermissions.Create,
            "创建配置",
            "create",
            10),
        new AuthorizationActionDefinition(
            "settings.config.update",
            "config-entries",
            ConfigEntryManagementPermissions.Update,
            "编辑配置",
            "update",
            20),
        new AuthorizationActionDefinition(
            "settings.config.disable",
            "config-entries",
            ConfigEntryManagementPermissions.Disable,
            "禁用配置",
            "disable",
            30),
    ];
}
