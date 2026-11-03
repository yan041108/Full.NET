namespace Full.NET.Modules.ImportExport.Persistence;

/// <summary>本人配置摘要固定前六列；详情才附加正文，不将私有配置带入任务列表。</summary>
internal sealed class ImportProfileRecord
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string HeaderFingerprint { get; init; } = "";
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
    public long Version { get; init; }
    public string? ConfigurationJson { get; init; }
}
