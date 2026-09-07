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
        LeaseId, LeaseExpiresAtUtc, Version
        """;

    private const string InsertedColumns = """
        inserted.Id, inserted.TenantId, inserted.SchemaKey, inserted.SchemaDisplayName, inserted.WorksheetKey, inserted.SourceFileId, inserted.SourceFileName, inserted.StatusKey, inserted.TotalRows, inserted.ValidRowCount, inserted.InvalidRowCount, inserted.PreviewRowsJson, inserted.ErrorCode, inserted.RequestedByUserId, inserted.CreatedAtUtc, inserted.PreviewCompletedAtUtc, inserted.ProcessedRowCount, inserted.SucceededRowCount, inserted.ExecutionFailedRowCount, inserted.NextLineNumber, inserted.ExecutionRowsJson, inserted.ErrorReceiptFileId, inserted.ExecutionStartedAtUtc, inserted.ExecutionCompletedAtUtc, inserted.LeaseId, inserted.LeaseExpiresAtUtc, inserted.Version
        """;

    /// <summary>可领取条件：排队中，或执行中但租约已到期/缺失（升级前崩溃行）。</summary>
    private const string ClaimablePredicate = """
        (StatusKey = 'queued'
         OR (StatusKey = 'executing'
             AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc <= @Now)))
        """;

    /// <summary>Worker 调度目录仅返回有可领取任务的租户标识，不授予读取任务或文件的权限。</summary>
    public static readonly SqlStatement ListPendingTenantIdsSqlServer = new(
        "import_export.task.pending_tenants.sqlserver",
        $"""
        SELECT TOP (@BatchSize) TenantId
        FROM fn_import_export_task
        WHERE {ClaimablePredicate}
        GROUP BY TenantId
        ORDER BY MIN(CreatedAtUtc), TenantId
        """, SqlDataScope.Global);

    /// <summary>MySQL Worker 调度目录；具体任务必须在可信租户上下文建立后领取。</summary>
    public static readonly SqlStatement ListPendingTenantIdsMySql = new(
        "import_export.task.pending_tenants.mysql",
        $"""
        SELECT TenantId
        FROM fn_import_export_task
        WHERE {ClaimablePredicate}
        GROUP BY TenantId
        ORDER BY MIN(CreatedAtUtc), TenantId
        LIMIT @BatchSize
        """, SqlDataScope.Global);

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
             @LeaseId, @LeaseExpiresAtUtc, @Version)
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindById = new(
        "import_export.task.find_by_id",
        $"""
        SELECT {SelectColumns}
        FROM fn_import_export_task
        WHERE TenantId = @TenantId AND Id = @Id
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement PageSqlServer = new(
        "import_export.task.page.sqlserver",
        $"""
        SELECT COUNT(1)
        FROM fn_import_export_task
        WHERE TenantId = @TenantId AND (@SchemaKey IS NULL OR SchemaKey = @SchemaKey);

        SELECT {SelectColumns}
        FROM fn_import_export_task
        WHERE TenantId = @TenantId AND (@SchemaKey IS NULL OR SchemaKey = @SchemaKey)
        ORDER BY CreatedAtUtc DESC, Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement PageMySql = new(
        "import_export.task.page.mysql",
        $"""
        SELECT COUNT(1)
        FROM fn_import_export_task
        WHERE TenantId = @TenantId AND (@SchemaKey IS NULL OR SchemaKey = @SchemaKey);

        SELECT {SelectColumns}
        FROM fn_import_export_task
        WHERE TenantId = @TenantId AND (@SchemaKey IS NULL OR SchemaKey = @SchemaKey)
        ORDER BY CreatedAtUtc DESC, Id DESC
        LIMIT @PageSize OFFSET @Offset;
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

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
            LeaseId = NULL,
            LeaseExpiresAtUtc = NULL,
            Version = Version + 1
        WHERE TenantId = @TenantId AND Id = @Id
          AND StatusKey = @ExpectedStatusKey
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ClaimQueuedSqlServer = new(
        "import_export.task.claim_queued.sqlserver",
        $"""
        WITH candidates AS (
            SELECT TOP (1) Id
            FROM fn_import_export_task WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE TenantId = @TenantId AND {ClaimablePredicate}
            ORDER BY CreatedAtUtc, Id
        )
        UPDATE task
        SET StatusKey = 'executing',
            ExecutionStartedAtUtc = COALESCE(task.ExecutionStartedAtUtc, @Now),
            LeaseId = @LeaseId,
            LeaseExpiresAtUtc = @LeaseExpiresAtUtc,
            Version = task.Version + 1
        OUTPUT {InsertedColumns}
        FROM fn_import_export_task AS task
        INNER JOIN candidates ON candidates.Id = task.Id
        WHERE task.TenantId = @TenantId;
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement SelectClaimableIdsMySql = new(
        "import_export.task.select_claimable_ids.mysql",
        $"""
        SELECT Id
        FROM fn_import_export_task
        WHERE TenantId = @TenantId AND {ClaimablePredicate}
        ORDER BY CreatedAtUtc, Id
        LIMIT 1
        FOR UPDATE SKIP LOCKED
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ClaimByIdsMySql = new(
        "import_export.task.claim_by_ids.mysql",
        $"""
        UPDATE fn_import_export_task
        SET StatusKey = 'executing',
            ExecutionStartedAtUtc = COALESCE(ExecutionStartedAtUtc, @Now),
            LeaseId = @LeaseId,
            LeaseExpiresAtUtc = @LeaseExpiresAtUtc,
            Version = Version + 1
        WHERE TenantId = @TenantId AND {ClaimablePredicate}
          AND Id IN @Ids
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement SelectByIds = new(
        "import_export.task.select_by_ids",
        $"""
        SELECT {SelectColumns}
        FROM fn_import_export_task
        WHERE TenantId = @TenantId AND Id IN @Ids
        ORDER BY CreatedAtUtc, Id
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

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
            LeaseId = CASE WHEN @StatusKey = 'queued' OR @StatusKey IN ('execution_succeeded', 'execution_partial', 'execution_failed') THEN NULL ELSE LeaseId END,
            LeaseExpiresAtUtc = CASE WHEN @StatusKey = 'queued' OR @StatusKey IN ('execution_succeeded', 'execution_partial', 'execution_failed') THEN NULL ELSE LeaseExpiresAtUtc END,
            Version = Version + 1
        WHERE TenantId = @TenantId AND Id = @Id
          AND StatusKey = 'executing'
          AND LeaseId = @LeaseId
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement MarkExecutionFailed = new(
        "import_export.task.mark_execution_failed",
        """
        UPDATE fn_import_export_task
        SET StatusKey = 'execution_failed',
            ErrorCode = @ErrorCode,
            ExecutionCompletedAtUtc = @ExecutionCompletedAtUtc,
            LeaseId = NULL,
            LeaseExpiresAtUtc = NULL,
            Version = Version + 1
        WHERE TenantId = @TenantId AND Id = @Id
          AND StatusKey = 'executing'
          AND LeaseId = @LeaseId
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
