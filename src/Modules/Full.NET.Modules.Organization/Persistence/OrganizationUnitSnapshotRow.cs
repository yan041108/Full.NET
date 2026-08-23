namespace Full.NET.Modules.Organization.Persistence;

/// <summary>机构单元投影回填与对账使用的最小只读快照行。</summary>
internal sealed class OrganizationUnitSnapshotRow
{
    public Guid UnitId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public int Version { get; set; }

    public DateTimeOffset ChangedAtUtc { get; set; }
}
