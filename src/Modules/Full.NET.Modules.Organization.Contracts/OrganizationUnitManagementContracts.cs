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
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="ParentId">父机构标识；顶级机构为 <see langword="null"/>。</param>
/// <param name="Code">机构编码；创建后不可变，需租户内唯一。</param>
/// <param name="Name">机构显示名称。</param>
/// <param name="DisplayOrder">同级内的排序权重；值越小越靠前。</param>
public sealed record CreateOrganizationUnitRequest(
    string? ParentId,
    string Code,
    string Name,
    int DisplayOrder);

/// <summary>更新租户机构请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="ParentId">父机构标识；顶级机构为 <see langword="null"/>。</param>
/// <param name="Name">机构显示名称。</param>
/// <param name="DisplayOrder">同级内的排序权重；值越小越靠前。</param>
/// <param name="Version">当前实体乐观锁版本；与服务端不一致时拒绝更新。</param>
public sealed record UpdateOrganizationUnitRequest(
    string? ParentId,
    string Name,
    int DisplayOrder,
    int Version);

/// <summary>租户机构列表项与详情响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">机构稳定标识。</param>
/// <param name="ParentId">父机构标识；顶级机构为 <see langword="null"/>。</param>
/// <param name="Code">机构编码；创建后不可变。</param>
/// <param name="Name">机构显示名称。</param>
/// <param name="DisplayOrder">同级内的排序权重；值越小越靠前。</param>
/// <param name="IsActive">是否启用；禁用后不再参与成员归属计算。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）；从未更新时为 <see langword="null"/>。</param>
/// <param name="Version">当前乐观锁版本；用于并发写控制。</param>
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
