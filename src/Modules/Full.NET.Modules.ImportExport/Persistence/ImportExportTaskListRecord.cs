namespace Full.NET.Modules.ImportExport.Persistence;

/// <summary>本人任务分页只读取公开摘要字段，不读取执行正文或原会话绑定。</summary>
internal sealed class ImportExportTaskListRecord
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string SchemaKey { get; init; } = string.Empty;
    public string SchemaDisplayName { get; init; } = string.Empty;
    public string WorksheetKey { get; init; } = string.Empty;
    public Guid SourceFileId { get; init; }
    public string? SourceFileName { get; init; }
    public required DateTimeOffset SourceExpiresAtUtc { get; init; }
    public DateTimeOffset? SourceReleaseStartedAtUtc { get; init; }
    public DateTimeOffset? SourceReleasedAtUtc { get; init; }
    public string StatusKey { get; init; } = string.Empty;
    public int TotalRows { get; init; }
    public int ValidRowCount { get; init; }
    public int InvalidRowCount { get; init; }
    public string? ErrorCode { get; init; }
    public Guid RequestedByUserId { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? PreviewCompletedAtUtc { get; init; }
    public int ProcessedRowCount { get; init; }
    public int SucceededRowCount { get; init; }
    public int ExecutionFailedRowCount { get; init; }
    public int NextLineNumber { get; init; }
    public Guid? ErrorReceiptFileId { get; init; }
    public DateTimeOffset? ExecutionStartedAtUtc { get; init; }
    public DateTimeOffset? ExecutionCompletedAtUtc { get; init; }
    public long Version { get; init; }
}
