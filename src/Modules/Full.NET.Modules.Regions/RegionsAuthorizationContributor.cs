using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Regions.Contracts;

namespace Full.NET.Modules.Regions;

/// <summary>
/// 向授权目录贡献行政区域模块的权限、导航与操作定义。
/// </summary>
internal sealed class RegionsAuthorizationContributor : IAuthorizationCatalogContributor
{
    private const AuthorizationScope ReadScopes =
        AuthorizationScope.Host | AuthorizationScope.Tenant;

    /// <summary>行政区域模块在授权目录中的定义。</summary>
    public AuthorizationModuleDefinition Module { get; } =
        new("regions", "行政区域", 67);

    /// <summary>行政区域模块全部权限定义。</summary>
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition(
            RegionsPermissions.Read,
            "查询行政区域",
            ReadScopes),
        new PermissionDefinition(
            RegionsPermissions.Manage,
            "进入行政区域维护管理页",
            AuthorizationScope.Host),
        new PermissionDefinition(
            RegionsPermissions.Create,
            "创建行政区域",
            AuthorizationScope.Host),
        new PermissionDefinition(
            RegionsPermissions.Update,
            "更新行政区域",
            AuthorizationScope.Host),
        new PermissionDefinition(
            RegionsPermissions.Delete,
            "删除行政区域",
            AuthorizationScope.Host),
        new PermissionDefinition(
            RegionsPermissions.Import,
            "导入行政区域数据集",
            AuthorizationScope.Host),
    ];

    /// <summary>行政区域模块导航定义。</summary>
    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "administrative-regions",
            null,
            "administrative-regions",
            "/regions/administrative-regions",
            "administrative-regions",
            "区域管理",
            "Administrative Regions",
            "regions",
            68,
            RegionsPermissions.Manage),
    ];

    /// <summary>行政区域模块页面操作定义。</summary>
    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "regions.administrative_regions.create",
            "administrative-regions",
            RegionsPermissions.Create,
            "创建区域",
            "create",
            10),
        new AuthorizationActionDefinition(
            "regions.administrative_regions.update",
            "administrative-regions",
            RegionsPermissions.Update,
            "编辑区域",
            "update",
            20),
        new AuthorizationActionDefinition(
            "regions.administrative_regions.delete",
            "administrative-regions",
            RegionsPermissions.Delete,
            "删除区域",
            "delete",
            30),
        new AuthorizationActionDefinition(
            "regions.administrative_regions.import",
            "administrative-regions",
            RegionsPermissions.Import,
            "导入区域数据",
            "import",
            40),
    ];
}
