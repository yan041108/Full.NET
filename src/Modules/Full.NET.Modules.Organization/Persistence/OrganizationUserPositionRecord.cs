namespace Full.NET.Modules.Organization.Persistence;

/// <summary>映射 <c>fn_organization_user_position</c> 行，表示用户在职位上的任职关系。</summary>
/// <remarks>主职位唯一性由管理服务在同一事务内清零旧主标记维护；<c>IsActive=0</c> 时 <c>IsPrimary</c> 必须同步清零。</remarks>
internal sealed class OrganizationUserPositionRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid PositionId { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public int Version { get; set; }
}

internal sealed class OrganizationUserPositionListRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid PositionId { get; set; }
    public string PositionCode { get; set; } = string.Empty;
    public string PositionName { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public int Version { get; set; }
}

internal sealed record InsertOrganizationUserPosition(
    Guid Id,
    Guid UserId,
    Guid PositionId,
    bool IsPrimary,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);
