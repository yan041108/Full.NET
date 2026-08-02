namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 定义页面内可授权的业务操作目录项。
/// </summary>
/// <param name="Id">全局唯一、稳定的目录项标识。</param>
/// <param name="NavigationId">已登记页面导航 ID。</param>
/// <param name="PermissionCode">已登记权限码。</param>
/// <param name="Name">供角色授权页展示的中文名称。</param>
/// <param name="ClientActionKey">Vue 本地操作白名单键，不是组件路径。</param>
/// <param name="Order">页面内稳定排序。</param>
public sealed record AuthorizationActionDefinition(
    string Id,
    string NavigationId,
    string PermissionCode,
    string Name,
    string ClientActionKey,
    int Order);