namespace Full.NET.Modules.Organization.Persistence;

/// <summary>映射 <c>fn_organization_position</c> 行，可选关联机构单元与职级用于展示。</summary>
/// <remarks>职位实体归属于当前租户；<c>UnitId</c> 与 <c>PositionLevelId</c> 为可空展示字段，缺失时不影响职位主体存在性。</remarks>
internal sealed record OrganizationPositionRecord(
    Guid Id,
    Guid TenantId,
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

internal sealed record InsertOrganizationPosition(
    Guid Id,
    string Code,
    string Name,
    int DisplayOrder,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

internal sealed class OrganizationPositionListRow
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public Guid? UnitId { get; set; }

    public string? UnitCode { get; set; }

    public string? UnitName { get; set; }

    public Guid? PositionLevelId { get; set; }

    public string? PositionLevelCode { get; set; }

    public string? PositionLevelName { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public int Version { get; set; }
}
