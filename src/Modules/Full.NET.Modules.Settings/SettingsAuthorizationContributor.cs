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
            EnumCatalogPermissions.Read,
            "查询枚举与常量目录",
            AuthorizationScope.Host),
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
    ];
}
