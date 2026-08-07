namespace Full.NET.Modules.Organization.Persistence;

internal sealed class OrganizationUnitSnapshotRow
{
    public Guid UnitId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public int Version { get; set; }

    public DateTimeOffset ChangedAtUtc { get; set; }
}
