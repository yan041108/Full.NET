namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 角色授权页使用的页面目录节点。
/// </summary>
/// <param name="Id">稳定页面标识。</param>
/// <param name="Title">中文页面标题。</param>
/// <param name="PermissionCode">页面读取权限码。</param>
/// <param name="Order">同级排序值。</param>
/// <param name="Actions">页面内可授权操作。</param>
/// <param name="Children">子页面。</param>
public sealed record AuthorizationTreePageResponse(
    string Id,
    string Title,
    string PermissionCode,
    int Order,
    IReadOnlyList<AuthorizationTreeActionResponse> Actions,
    IReadOnlyList<AuthorizationTreePageResponse> Children);

/// <summary>
/// 角色授权页使用的页面操作节点。
/// </summary>
/// <param name="Id">稳定操作目录标识。</param>
/// <param name="Name">中文操作名称。</param>
/// <param name="PermissionCode">操作权限码。</param>
/// <param name="Order">页面内排序值。</param>
public sealed record AuthorizationTreeActionResponse(
    string Id,
    string Name,
    string PermissionCode,
    int Order);