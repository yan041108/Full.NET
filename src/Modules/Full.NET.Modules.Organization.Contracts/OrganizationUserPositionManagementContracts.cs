namespace Full.NET.Modules.Organization.Contracts;

/// <summary>
/// 租户用户-职位隶属管理 API 契约。
/// </summary>
public static class OrganizationUserPositionManagementPermissions
{
    /// <summary>分页查询用户-职位隶属。</summary>
    public const string Read = "organization.user_positions.read";

    /// <summary>分配用户-职位隶属。</summary>
    public const string Create = "organization.user_positions.create";

    /// <summary>设为主职位。</summary>
    public const string Update = "organization.user_positions.update";

    /// <summary>取消用户-职位隶属。</summary>
    public const string Disable = "organization.user_positions.disable";

    /// <summary>迁移 065 前遗留的粗粒度写权限；不再进入可分配目录。</summary>
    public const string Write = "organization.user_positions.write";
}

/// <summary>创建用户-职位隶属请求。</summary>
public sealed record CreateOrganizationUserPositionRequest(
    Guid UserId,
    Guid PositionId,
    bool IsPrimary);

/// <summary>更新用户-职位隶属请求。</summary>
public sealed record UpdateOrganizationUserPositionRequest(
    bool IsPrimary,
    int Version);

/// <summary>用户-职位隶属列表项与详情。</summary>
public sealed record OrganizationUserPositionResponse(
    Guid Id,
    Guid UserId,
    string Username,
    string DisplayName,
    Guid PositionId,
    string PositionCode,
    string PositionName,
    bool IsPrimary,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);
