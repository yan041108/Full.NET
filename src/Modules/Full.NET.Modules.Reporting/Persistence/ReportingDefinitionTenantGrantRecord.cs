namespace Full.NET.Modules.Reporting.Persistence;

/// <summary>本模块显式授权账本；不建立到 Tenancy 表的跨模块外键。</summary>
internal sealed class ReportingDefinitionTenantGrantRecord
{
    public Guid Id { get; set; }
    public Guid DefinitionId { get; set; }
    public Guid TenantId { get; set; }
    public bool IsEnabled { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
