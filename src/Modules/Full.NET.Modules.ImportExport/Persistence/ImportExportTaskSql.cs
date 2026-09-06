using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.ImportExport.Persistence;

/// <summary>静态 Schema 导入任务 SQL 语句。</summary>
internal static class ImportExportTaskSql
{
    private const string SelectColumns = """
        Id, TenantId, SchemaKey, SchemaDisplayName, WorksheetKey, SourceFileId, SourceFileName,
        StatusKey, TotalRows, ValidRowCount, InvalidRowCount, PreviewRowsJson, ErrorCode,
        RequestedByUserId, CreatedAtUtc, PreviewCompletedAtUtc,
        ProcessedRowCount, SucceededRowCount, ExecutionFailedRowCount, NextLineNumber,
        ExecutionRowsJson, ErrorReceiptFileId, ExecutionStartedAtUtc, ExecutionCompletedAtUtc,
        Version
        """;

    public static readonly SqlStatement Insert = new(
        "import_export.task.insert",
        $"""
        INSERT INTO fn_import_export_task
            ({SelectColumns})
        VALUES
            (@Id, @TenantId, @SchemaKey, @SchemaDisplayName, @WorksheetKey, @SourceFileId, @SourceFileName,
             @StatusKey, @TotalRows, @ValidRowCount, @InvalidRowCount, @PreviewRowsJson, @ErrorCode,
             @RequestedByUserId, @CreatedAtUtc, @PreviewCompletedAtUtc,
             @ProcessedRowCount, @SucceededRowCount, @ExecutionFailedRowCount, @NextLineNumber,
             @ExecutionRowsJson, @ErrorReceiptFileId, @ExecutionStartedAtUtc, @ExecutionCompletedAtUtc,
             @Version)
        """,
        SqlDataScope.TenantRequired);

    public static readonly SqlStatement FindById = new(
        "import_export.task.find_by_id",
        $"""
        SELECT {SelectColumns}
        FROM fn_import_export_task
        WHERE Id = @Id
        """,
        SqlDataScope.TenantRequired);

    public static readonly SqlStatement PageSqlServer = new(
        "import_export.task.page.sqlserver",
        $"""
        SELECT COUNT(1)
        FROM fn_import_export_task
        WHERE (@SchemaKey IS NULL OR SchemaKey = @SchemaKey);

        SELECT {SelectColumns}
        FROM fn_import_export_task
        WHERE (@SchemaKey IS NULL OR SchemaKey = @SchemaKey)
        ORDER BY CreatedAtUtc DESC, Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
        """,
        SqlDataScope.TenantRequired);

    public static readonly SqlStatement PageMySql = new(
        "import_export.task.page.mysql",
        $"""
        SELECT COUNT(1)
        FROM fn_import_export_task
        WHERE (@SchemaKey IS NULL OR SchemaKey = @SchemaKey);

        SELECT {SelectColumns}
        FROM fn_import_export_task
        WHERE (@SchemaKey IS NULL OR SchemaKey = @SchemaKey)
        ORDER BY CreatedAtUtc DESC, Id DESC
        LIMIT @PageSize OFFSET @Offset;
        """,
        SqlDataScope.TenantRequired);

    public static readonly SqlStatement QueueExecution = new(
        "import_export.task.queue_execution",
        """
        UPDATE fn_import_export_task
        SET StatusKey = @StatusKey,
            NextLineNumber = @NextLineNumber,
            ProcessedRowCount = @ProcessedRowCount,
            SucceededRowCount = @SucceededRowCount,
            ExecutionFailedRowCount = @ExecutionFailedRowCount,
            ExecutionRowsJson = @ExecutionRowsJson,
            ErrorReceiptFileId = @ErrorReceiptFileId,
            ExecutionStartedAtUtc = @ExecutionStartedAtUtc,
            ExecutionCompletedAtUtc = @ExecutionCompletedAtUtc,
            ErrorCode = @ErrorCode,
            Version = Version + 1
        WHERE Id = @Id
          AND StatusKey = @ExpectedStatusKey
        """,
        SqlDataScope.TenantRequired);

    public static readonly SqlStatement ClaimQueuedSqlServer = new(
        "import_export.task.claim_queued.sqlserver",
        $"""
        WITH candidates AS (
            SELECT TOP (@BatchSize) Id
            FROM fn_import_export_task
            WHERE StatusKey = 'queued'
            ORDER BY CreatedAtUtc, Id
        )
        UPDATE task
        SET StatusKey = 'executing',
            ExecutionStartedAtUtc = COALESCE(task.ExecutionStartedAtUtc, @Now),
            Version = task.Version + 1
        OUTPUT {SelectColumns}
        FROM fn_import_export_task AS task
        INNER JOIN candidates ON candidates.Id = task.Id;
        """,
        SqlDataScope.TenantRequired);

    public static readonly SqlStatement SelectClaimableIdsMySql = new(
        "import_export.task.select_claimable_ids.mysql",
        """
        SELECT Id
        FROM fn_import_export_task
        WHERE StatusKey = 'queued'
        ORDER BY CreatedAtUtc, Id
        LIMIT @BatchSize
        """,
        SqlDataScope.TenantRequired);

    public static readonly SqlStatement ClaimByIdsMySql = new(
        "import_export.task.claim_by_ids.mysql",
        """
        UPDATE fn_import_export_task
        SET StatusKey = 'executing',
            ExecutionStartedAtUtc = COALESCE(ExecutionStartedAtUtc, @Now),
            Version = Version + 1
        WHERE StatusKey = 'queued'
          AND Id IN @Ids
        """,
        SqlDataScope.TenantRequired);

    public static readonly SqlStatement SelectByIds = new(
        "import_export.task.select_by_ids",
        $"""
        SELECT {SelectColumns}
        FROM fn_import_export_task
        WHERE Id IN @Ids
        ORDER BY CreatedAtUtc, Id
        """,
        SqlDataScope.TenantRequired);

    public static readonly SqlStatement UpdateExecutionProgress = new(
        "import_export.task.update_execution_progress",
        """
        UPDATE fn_import_export_task
        SET StatusKey = @StatusKey,
            ProcessedRowCount = @ProcessedRowCount,
            SucceededRowCount = @SucceededRowCount,
            ExecutionFailedRowCount = @ExecutionFailedRowCount,
            NextLineNumber = @NextLineNumber,
            ExecutionRowsJson = @ExecutionRowsJson,
            ErrorReceiptFileId = @ErrorReceiptFileId,
            ExecutionCompletedAtUtc = @ExecutionCompletedAtUtc,
            ErrorCode = @ErrorCode,
            Version = Version + 1
        WHERE Id = @Id
          AND StatusKey = 'executing'
        """,
        SqlDataScope.TenantRequired);

    public static readonly SqlStatement MarkExecutionFailed = new(
        "import_export.task.mark_execution_failed",
        """
        UPDATE fn_import_export_task
        SET StatusKey = 'execution_failed',
            ErrorCode = @ErrorCode,
            ExecutionCompletedAtUtc = @ExecutionCompletedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND StatusKey = 'executing'
        """,
        SqlDataScope.TenantRequired);
}
