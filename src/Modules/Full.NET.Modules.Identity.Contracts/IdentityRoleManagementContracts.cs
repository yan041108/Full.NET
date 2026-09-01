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
/// <param name="Code">稳定角色编码；在 Host 作用域内唯一且不可更改。</param>
/// <param name="Name">面向管理员展示的角色名称。</param>
public sealed record CreateHostRoleRequest(
    string Code,
    string Name);

/// <summary>更新 Host 角色显示名称请求。</summary>
/// <param name="Name">更新后的角色展示名称。</param>
/// <param name="Version">调用方看到的当前版本；服务端据此拒绝并发覆盖。</param>
public sealed record UpdateHostRoleRequest(
    string Name,
    int Version);

/// <summary>替换 Host 角色权限集合请求。</summary>
/// <param name="PermissionCodes">提交后应完整生效的稳定权限码；调用方应按服务端目录整量覆盖而非增量差异。</param>
/// <param name="Version">调用方看到的当前版本；服务端据此拒绝并发覆盖。</param>
public sealed record ReplaceHostRolePermissionsRequest(
    IReadOnlyList<string> PermissionCodes,
    int Version);

/// <summary>Host 角色列表项与详情响应。</summary>
/// <param name="Id">角色稳定标识。</param>
/// <param name="Code">稳定角色编码。</param>
/// <param name="Name">角色展示名称。</param>
/// <param name="IsSystem">是否为系统受保护角色；系统角色禁止编辑、禁用或删除。</param>
/// <param name="IsActive">是否处于活动状态；禁用角色不再参与授权计算。</param>
/// <param name="IsSuperAdministrator">是否为受保护的超级管理员角色；该角色授权由独立高风险写路径维护。</param>
/// <param name="PermissionCodes">当前整量生效的稳定权限码集合。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近一次更新时间（UTC）；从未更新时为 <see langword="null"/>。</param>
/// <param name="Version">乐观并发版本。</param>
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
