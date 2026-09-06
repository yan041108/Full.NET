namespace Full.NET.Modules.ImportExport.Persistence;

/// <summary>静态 Schema 导入任务持久化记录。</summary>
internal sealed class ImportExportTaskRecord
{
    public Guid Id { get; init; }

    public Guid TenantId { get; init; }

    public string SchemaKey { get; init; } = string.Empty;

    public string SchemaDisplayName { get; init; } = string.Empty;

    public string WorksheetKey { get; init; } = string.Empty;

    public Guid SourceFileId { get; init; }

    public string? SourceFileName { get; init; }

    public string StatusKey { get; init; } = string.Empty;

    public int TotalRows { get; init; }

    public int ValidRowCount { get; init; }

    public int InvalidRowCount { get; init; }

    public string? PreviewRowsJson { get; init; }

    public string? ErrorCode { get; init; }

    public Guid RequestedByUserId { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? PreviewCompletedAtUtc { get; init; }

    public long Version { get; init; }
}
