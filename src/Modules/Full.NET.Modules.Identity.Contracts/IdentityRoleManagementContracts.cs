namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// Host 作用域角色管理 API 的请求与响应契约（纵向切片 Task 1 冻结）。
/// </summary>
public static class IdentityRoleManagementPermissions
{
    /// <summary>分页查询 Host 角色列表与详情。</summary>
    public const string Read = "identity.roles.read";

    /// <summary>创建 Host 角色。</summary>
    public const string Create = "identity.roles.create";

    /// <summary>更新 Host 角色显示名称。</summary>
    public const string Update = "identity.roles.update";

    /// <summary>替换 Host 角色权限集合。</summary>
    public const string AssignPermissions = "identity.roles.assign_permissions";

    /// <summary>禁用 Host 角色。</summary>
    public const string Disable = "identity.roles.disable";

    /// <summary>更新 Host 角色数据范围。</summary>
    public const string AssignDataScope = "identity.roles.assign_data_scope";

    /// <summary>迁移 055 前遗留的粗粒度写权限；不再进入可分配目录。</summary>
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
