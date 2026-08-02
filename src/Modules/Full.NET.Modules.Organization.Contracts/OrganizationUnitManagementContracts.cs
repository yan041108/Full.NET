namespace Full.NET.Modules.Organization.Contracts;

/// <summary>
/// 租户机构管理 API 的请求与响应契约（纵向切片 Task 1 冻结）。
/// </summary>
public static class OrganizationUnitManagementPermissions
{
    /// <summary>分页查询租户机构列表与详情。</summary>
    public const string Read = "organization.units.read";

    /// <summary>创建租户机构。</summary>
    public const string Create = "organization.units.create";

    /// <summary>更新租户机构。</summary>
    public const string Update = "organization.units.update";

    /// <summary>禁用租户机构。</summary>
    public const string Disable = "organization.units.disable";

    /// <summary>迁移 062 前遗留的粗粒度写权限；不再进入可分配目录。</summary>
    public const string Write = "organization.units.write";
}

/// <summary>创建租户机构请求。</summary>
public sealed record CreateOrganizationUnitRequest(
    string? ParentId,
    string Code,
    string Name,
    int DisplayOrder);

/// <summary>更新租户机构请求。</summary>
public sealed record UpdateOrganizationUnitRequest(
    string? ParentId,
    string Name,
    int DisplayOrder,
    int Version);

/// <summary>租户机构列表项与详情响应。</summary>
public sealed record OrganizationUnitResponse(
    Guid Id,
    Guid? ParentId,
    string Code,
    string Name,
    int DisplayOrder,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);
