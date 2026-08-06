namespace Full.NET.Modules.Organization.Persistence;

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
