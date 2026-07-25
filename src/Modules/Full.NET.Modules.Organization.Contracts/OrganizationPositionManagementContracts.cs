namespace Full.NET.Modules.Organization.Contracts;

/// <summary>
/// 租户职位管理 API 的请求与响应契约。
/// </summary>
public static class OrganizationPositionManagementPermissions
{
    /// <summary>分页查询租户职位列表与详情。</summary>
    public const string Read = "organization.positions.read";

    /// <summary>创建、更新与禁用租户职位。</summary>
    public const string Write = "organization.positions.write";
}

/// <summary>创建租户职位请求。</summary>
public sealed record CreateOrganizationPositionRequest(
    string Code,
    string Name,
    int DisplayOrder);

/// <summary>更新租户职位请求。</summary>
public sealed record UpdateOrganizationPositionRequest(
    string Name,
    int DisplayOrder,
    int Version);

/// <summary>租户职位列表项与详情响应。</summary>
public sealed record OrganizationPositionResponse(
    Guid Id,
    string Code,
    string Name,
    int DisplayOrder,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);
