namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// Host 作用域角色管理 API 的请求与响应契约（纵向切片 Task 1 冻结）。
/// </summary>
public static class IdentityRoleManagementPermissions
{
    /// <summary>分页查询 Host 角色列表与详情。</summary>
    public const string Read = "identity.roles.read";

    /// <summary>创建、更新、替换权限与禁用 Host 角色。</summary>
    public const string Write = "identity.roles.write";
}

/// <summary>创建 Host 角色请求。</summary>
public sealed record CreateHostRoleRequest(
    string Code,
    string Name);

/// <summary>更新 Host 角色显示名称请求。</summary>
public sealed record UpdateHostRoleRequest(
    string Name,
    int Version);

/// <summary>替换 Host 角色权限集合请求。</summary>
public sealed record ReplaceHostRolePermissionsRequest(
    IReadOnlyList<string> PermissionCodes,
    int Version);

/// <summary>Host 角色列表项与详情响应。</summary>
public sealed record HostRoleResponse(
    Guid Id,
    string Code,
    string Name,
    bool IsSystem,
    bool IsActive,
    bool IsSuperAdministrator,
    IReadOnlyList<string> PermissionCodes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);
