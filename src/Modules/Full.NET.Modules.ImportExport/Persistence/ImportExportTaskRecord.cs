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

    /// <summary>创建任务时固定的源文件期限，不用于删除业务事实或错误回执。</summary>
    public required DateTimeOffset SourceExpiresAtUtc { get; init; }

    /// <summary>清理已开始；即使对象删除失败，也不能重新把原文件排队执行。</summary>
    public DateTimeOffset? SourceReleaseStartedAtUtc { get; init; }

    /// <summary>文件提供程序已确认幂等释放的时间。</summary>
    public DateTimeOffset? SourceReleasedAtUtc { get; init; }

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

    /// <summary>创建时固化的列映射；执行、恢复和重试均不能由当前个人配置替换。</summary>
    public string? ColumnMappingJson { get; init; }

    /// <summary>本人显式排队操作绑定的会话；后台每批与行写入前重新确认其当前授权。</summary>
    public string? ActorSessionBindingJson { get; init; }

    public Guid? ErrorReceiptFileId { get; init; }

    public DateTimeOffset? ExecutionStartedAtUtc { get; init; }

    public DateTimeOffset? ExecutionCompletedAtUtc { get; init; }

    /// <summary>当前执行租约；未领取或已完成后为空。</summary>
    public Guid? LeaseId { get; init; }

    /// <summary>租约到期时间；到期后允许其他 Worker 重新领取。</summary>
    public DateTimeOffset? LeaseExpiresAtUtc { get; init; }

    public long Version { get; init; }
}
