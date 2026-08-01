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
            DictTypeManagementPermissions.Write,
            "管理数据字典",
            AuthorizationScope.Host),
        new PermissionDefinition(
            ConfigEntryManagementPermissions.Read,
            "查询系统配置",
            AuthorizationScope.Host),
        new PermissionDefinition(
            ConfigEntryManagementPermissions.Write,
            "管理系统配置",
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
            TenantDictTypeManagementPermissions.Write,
            "管理租户数据字典",
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
}
