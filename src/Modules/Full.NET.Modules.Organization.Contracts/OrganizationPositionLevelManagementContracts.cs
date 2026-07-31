namespace Full.NET.Modules.Organization.Contracts;

/// <summary>
/// 租户职级管理 API 的权限契约。
/// </summary>
public static class OrganizationPositionLevelManagementPermissions
{
    /// <summary>分页查询租户职级列表与详情。</summary>
    public const string Read = "organization.position_levels.read";

    /// <summary>创建、更新与禁用租户职级。</summary>
    public const string Write = "organization.position_levels.write";
}

/// <summary>创建租户职级请求。</summary>
public sealed record CreateOrganizationPositionLevelRequest(
    string Code,
    string Name,
    int DisplayOrder);

/// <summary>更新租户职级请求。</summary>
public sealed record UpdateOrganizationPositionLevelRequest(
    string Name,
    int DisplayOrder,
    int Version);

/// <summary>租户职级列表项与详情响应。</summary>
public sealed record OrganizationPositionLevelResponse(
    Guid Id,
    string Code,
    string Name,
    int DisplayOrder,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);
