namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 允许业务模块向授权目录贡献稳定权限和导航定义。
/// </summary>
public interface IAuthorizationCatalogContributor
{
    /// <summary>
    /// 获取当前贡献者所属的授权模块定义。
    /// </summary>
    AuthorizationModuleDefinition Module { get; }

    /// <summary>
    /// 获取当前模块拥有的权限定义。
    /// </summary>
    IReadOnlyCollection<PermissionDefinition> Permissions { get; }

    /// <summary>
    /// 获取当前模块拥有的导航定义。
    /// </summary>
    IReadOnlyCollection<NavigationDefinition> Navigation { get; }

    /// <summary>
    /// 获取当前模块拥有的页面操作定义；未迁移模块保持空集合。
    /// </summary>
    IReadOnlyCollection<AuthorizationActionDefinition> Actions => [];
}
