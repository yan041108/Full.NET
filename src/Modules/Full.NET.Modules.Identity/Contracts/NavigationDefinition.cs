namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 定义由模块贡献的导航投影元数据。
/// </summary>
/// <param name="Id">全局唯一且稳定的导航标识。</param>
/// <param name="ParentId">可空的父导航标识。</param>
/// <param name="RouteName">客户端路由名称。</param>
/// <param name="Path">客户端路由路径。</param>
/// <param name="ComponentKey">客户端本地组件白名单键。</param>
/// <param name="Title">中文导航标题。</param>
/// <param name="Caption">辅助说明。</param>
/// <param name="Icon">客户端图标语义键。</param>
/// <param name="Order">同级稳定排序值。</param>
/// <param name="RequiredPermission">显示该导航所需的权限码。</param>
public sealed record NavigationDefinition(
    string Id,
    string? ParentId,
    string RouteName,
    string Path,
    string ComponentKey,
    string Title,
    string Caption,
    string Icon,
    int Order,
    string RequiredPermission);
