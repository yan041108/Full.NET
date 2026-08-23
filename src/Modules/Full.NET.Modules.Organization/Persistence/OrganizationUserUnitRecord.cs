namespace Full.NET.Modules.Organization.Persistence;

/// <summary>映射 <c>fn_organization_user_unit</c> 行，表示用户在机构单元的隶属关系。</summary>
/// <remarks>主部门唯一性由管理服务在同一事务内清零旧主标记维护；<c>IsActive=0</c> 时 <c>IsPrimary</c> 必须同步清零。</remarks>
internal sealed class OrganizationUserUnitRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid UnitId { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public int Version { get; set; }
}

internal sealed class OrganizationUserUnitListRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public int Version { get; set; }
}

internal sealed record InsertOrganizationUserUnit(
    Guid Id,
    Guid UserId,
    Guid UnitId,
    bool IsPrimary,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);
