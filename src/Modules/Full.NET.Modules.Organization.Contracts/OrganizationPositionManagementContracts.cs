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

/// <summary>绑定或解绑职位所属机构；空机构标识表示解除现有绑定。</summary>
public sealed record AssignOrganizationPositionUnitRequest(
    Guid? UnitId,
    int Version);

/// <summary>绑定或解绑职位所属职级；空职级标识表示解除现有绑定。</summary>
public sealed record AssignOrganizationPositionLevelRequest(
    Guid? PositionLevelId,
    int Version);

/// <summary>租户职位列表项与详情响应。</summary>
public sealed record OrganizationPositionResponse(
    Guid Id,
    string Code,
    string Name,
    Guid? UnitId,
    string? UnitCode,
    string? UnitName,
    Guid? PositionLevelId,
    string? PositionLevelCode,
    string? PositionLevelName,
    int DisplayOrder,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);
