namespace Full.NET.Modules.Identity.Features.OrganizationUnitProjection;

internal sealed class OrganizationUnitProjectionRecord
{
    public Guid UnitId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public long SourceVersion { get; set; }
}
