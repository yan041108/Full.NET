namespace Full.NET.Modules.Organization.Persistence;

/// <summary>映射 <c>fn_organization_position_level</c> 行，承载租户内独立职级序列。</summary>
internal sealed record OrganizationPositionLevelRecord(
    Guid Id,
    Guid TenantId,
    string Code,
    string Name,
    int DisplayOrder,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

internal sealed record InsertOrganizationPositionLevel(
    Guid Id,
    string Code,
    string Name,
    int DisplayOrder,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);
