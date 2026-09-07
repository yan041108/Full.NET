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

    public int ProcessedRowCount { get; init; }

    public int SucceededRowCount { get; init; }

    public int ExecutionFailedRowCount { get; init; }

    public int NextLineNumber { get; init; }

    public string? ExecutionRowsJson { get; init; }

    public Guid? ErrorReceiptFileId { get; init; }

    public DateTimeOffset? ExecutionStartedAtUtc { get; init; }

    public DateTimeOffset? ExecutionCompletedAtUtc { get; init; }

    /// <summary>当前执行租约；未领取或已完成后为空。</summary>
    public Guid? LeaseId { get; init; }

    /// <summary>租约到期时间；到期后允许其他 Worker 重新领取。</summary>
    public DateTimeOffset? LeaseExpiresAtUtc { get; init; }

    public long Version { get; init; }
}
