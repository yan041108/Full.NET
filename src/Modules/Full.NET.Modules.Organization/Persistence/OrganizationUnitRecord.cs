namespace Full.NET.Modules.Organization.Persistence;

/// <summary>映射 <c>fn_organization_unit</c> 行，承载租户机构单元父子层级与软禁用状态。</summary>
/// <remarks>父子环不变量由管理服务在写入前完成检测，不在 SQL 层强制；<c>ParentId</c> 为 <c>null</c> 表示租户根节点。</remarks>
internal sealed record OrganizationUnitRecord(
    Guid Id,
    Guid TenantId,
    Guid? ParentId,
    string Code,
    string Name,
    int DisplayOrder,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

internal sealed record InsertOrganizationUnit(
    Guid Id,
    Guid? ParentId,
    string Code,
    string Name,
    int DisplayOrder,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

internal sealed class OrganizationUnitListRow
{
    public Guid Id { get; set; }

    public Guid? ParentId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public int Version { get; set; }
}

/// <summary>仅用于环检测的 Id/ParentId 投影。</summary>
internal sealed class OrganizationUnitParentLink
{
    public Guid Id { get; set; }

    public Guid? ParentId { get; set; }
}
