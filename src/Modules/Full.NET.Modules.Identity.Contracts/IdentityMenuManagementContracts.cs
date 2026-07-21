namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// Host 作用域菜单管理 API 的请求与响应契约（纵向切片 Task 1 冻结）。
/// </summary>
public static class IdentityMenuManagementPermissions
{
    /// <summary>分页查询 Host 菜单列表与详情。</summary>
    public const string Read = "identity.menus.read";

    /// <summary>创建、更新与禁用 Host 菜单。</summary>
    public const string Write = "identity.menus.write";
}

/// <summary>创建 Host 菜单请求。</summary>
public sealed record CreateHostMenuRequest(
    string? ParentId,
    string RouteName,
    string Path,
    string ComponentKey,
    string Title,
    string Caption,
    string Icon,
    int DisplayOrder,
    string RequiredPermission);

/// <summary>更新 Host 菜单请求。</summary>
public sealed record UpdateHostMenuRequest(
    string? ParentId,
    string Path,
    string ComponentKey,
    string Title,
    string Caption,
    string Icon,
    int DisplayOrder,
    string RequiredPermission,
    int Version);

/// <summary>Host 菜单列表项与详情响应。</summary>
public sealed record HostMenuResponse(
    Guid Id,
    Guid? ParentId,
    string RouteName,
    string Path,
    string ComponentKey,
    string Title,
    string Caption,
    string Icon,
    int DisplayOrder,
    string RequiredPermission,
    bool IsSystem,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);
