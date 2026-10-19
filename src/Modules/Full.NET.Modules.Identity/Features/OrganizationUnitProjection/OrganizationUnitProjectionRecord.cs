/// <summary>Identity 持久化行投影；仅供 Dapper 物化，不直接作为 HTTP 契约。</summary>
namespace Full.NET.Modules.Identity.Features.OrganizationUnitProjection;

internal sealed class OrganizationUnitProjectionRecord
{
    public Guid UnitId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public long SourceVersion { get; set; }
}
