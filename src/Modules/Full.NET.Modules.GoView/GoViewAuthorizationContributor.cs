using Full.NET.Modules.GoView.Contracts;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.GoView;

/// <summary>向 Identity 授权目录注册 GoView 权限、导航与页面操作。</summary>
internal sealed class GoViewAuthorizationContributor : IAuthorizationCatalogContributor
{
    public AuthorizationModuleDefinition Module { get; } =
        new("goview", "GoView", 120);

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new(GoViewProjectPermissions.Read, "读取大屏项目", AuthorizationScope.Host),
        new(GoViewProjectPermissions.Create, "创建大屏项目", AuthorizationScope.Host),
        new(GoViewProjectPermissions.Update, "更新大屏项目草稿", AuthorizationScope.Host),
        new(GoViewProjectPermissions.Publish, "发布大屏项目版本", AuthorizationScope.Host),
        new(GoViewProjectPermissions.Preview, "预览已发布大屏项目", AuthorizationScope.Host),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "goview-projects",
            null,
            "goview-projects",
            "/goview/projects",
            "goview-projects",
            "大屏项目",
            "GoView Projects",
            "monitor",
            10,
            GoViewProjectPermissions.Read),
    ];

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "goview.projects.create",
            "goview-projects",
            GoViewProjectPermissions.Create,
            "创建项目",
            "create",
            10),
        new AuthorizationActionDefinition(
            "goview.projects.update",
            "goview-projects",
            GoViewProjectPermissions.Update,
            "编辑画布",
            "update",
            20),
        new AuthorizationActionDefinition(
            "goview.projects.publish",
            "goview-projects",
            GoViewProjectPermissions.Publish,
            "发布版本",
            "publish",
            30),
        new AuthorizationActionDefinition(
            "goview.projects.preview",
            "goview-projects",
            GoViewProjectPermissions.Preview,
            "只读预览",
            "preview",
            40),
    ];
}
