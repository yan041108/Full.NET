namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// Host 作用域用户管理 API 的请求与响应契约（纵向切片 Task 1 冻结）。
/// </summary>
public static class IdentityUserManagementPermissions
{
    /// <summary>分页查询 Host 用户列表与详情。</summary>
    public const string Read = "identity.users.read";

    /// <summary>创建、更新、禁用与启用 Host 用户。</summary>
    public const string Write = "identity.users.write";

    /// <summary>按当前字段投影导出 Host 用户。</summary>
    public const string Export = "identity.users.export";
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

/// <summary>管理员重置 Host 用户密码请求。</summary>
public sealed record ResetHostUserPasswordRequest(
    string Password);

/// <summary>Host 用户列表项与详情响应。</summary>
public sealed record HostUserResponse(
    Guid Id,
    string Username,
    string DisplayName,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version,
    HostUserProjectedFieldsResponse? ProjectedFields = null);

/// <summary>
/// Host 用户的受限投影；EffectiveFieldKeys 用于区分无授权与有授权但值为空。
/// </summary>
public sealed record HostUserProjectedFieldsResponse(
    IReadOnlyList<string> EffectiveFieldKeys,
    string? PreferredLocale,
    int? FailedLoginCount,
    DateTimeOffset? LockoutEndUtc);
