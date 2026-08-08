namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 表示已经过服务端权限裁剪的客户端导航节点。
/// </summary>
/// <param name="Id">稳定导航标识。</param>
/// <param name="ParentId">可空父节点标识。</param>
/// <param name="RouteName">客户端路由名称。</param>
/// <param name="Path">客户端路由路径。</param>
/// <param name="ComponentKey">客户端本地组件白名单键。</param>
/// <param name="Title">中文标题。</param>
/// <param name="Caption">辅助说明。</param>
/// <param name="Icon">图标语义键。</param>
/// <param name="Order">同级排序值。</param>
/// <param name="RequiredPermission">显示节点所需的权限码。</param>
/// <param name="Children">经过裁剪的子节点。</param>
/// <param name="MenuType">菜单类型。</param>
/// <param name="Redirect">可选重定向路径。</param>
/// <param name="LinkUrl">可选外链或内嵌地址。</param>
/// <param name="IsHidden">是否在侧栏隐藏。</param>
/// <param name="IsKeepAlive">是否缓存页面实例。</param>
/// <param name="IsAffix">是否固定在标签栏。</param>
/// <param name="IsEmbedded">是否以内嵌方式打开。</param>
public sealed record NavigationNodeResponse(
    string Id,
    string? ParentId,
    string RouteName,
    string Path,
    string ComponentKey,
    string Title,
    string Caption,
    string Icon,
    int Order,
    string RequiredPermission,
    IReadOnlyList<NavigationNodeResponse> Children,
    string MenuType = IdentityHostMenuTypes.Menu,
    string? Redirect = null,
    string? LinkUrl = null,
    bool IsHidden = false,
    bool IsKeepAlive = false,
    bool IsAffix = false,
    bool IsEmbedded = false);
