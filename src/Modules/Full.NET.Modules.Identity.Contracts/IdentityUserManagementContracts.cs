namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// Host 作用域用户管理 API 的请求与响应契约（纵向切片 Task 1 冻结）。
/// </summary>
public static class IdentityUserManagementPermissions
{
    /// <summary>分页查询 Host 用户列表与详情。</summary>
    public const string Read = "identity.users.read";

    /// <summary>创建、更新与禁用 Host 用户。</summary>
    public const string Write = "identity.users.write";
}

/// <summary>创建 Host 用户请求。</summary>
public sealed record CreateHostUserRequest(
    string Username,
    string DisplayName,
    string Password);

/// <summary>更新 Host 用户基础资料请求。</summary>
public sealed record UpdateHostUserRequest(
    string DisplayName,
    int Version);

/// <summary>Host 用户列表项与详情响应。</summary>
public sealed record HostUserResponse(
    Guid Id,
    string Username,
    string DisplayName,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);
