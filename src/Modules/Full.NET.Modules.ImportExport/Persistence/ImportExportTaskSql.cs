using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.ImportExport.Persistence;

/// <summary>静态 Schema 导入任务 SQL 语句。</summary>
internal static class ImportExportTaskSql
{
    private const string SelectColumns = """
        Id, TenantId, SchemaKey, SchemaDisplayName, WorksheetKey, SourceFileId, SourceFileName, SourceExpiresAtUtc, SourceReleaseStartedAtUtc, SourceReleasedAtUtc,
        StatusKey, TotalRows, ValidRowCount, InvalidRowCount, PreviewRowsJson, ErrorCode,
        RequestedByUserId, CreatedAtUtc, PreviewCompletedAtUtc,
        ProcessedRowCount, SucceededRowCount, ExecutionFailedRowCount, NextLineNumber,
        ExecutionRowsJson, ErrorReceiptFileId, ExecutionStartedAtUtc, ExecutionCompletedAtUtc,
        LeaseId, LeaseExpiresAtUtc, Version, ColumnMappingJson, ActorSessionBindingJson
        """;

    private const string InsertedColumns = """
        inserted.Id, inserted.TenantId, inserted.SchemaKey, inserted.SchemaDisplayName, inserted.WorksheetKey, inserted.SourceFileId, inserted.SourceFileName, inserted.SourceExpiresAtUtc, inserted.SourceReleaseStartedAtUtc, inserted.SourceReleasedAtUtc, inserted.StatusKey, inserted.TotalRows, inserted.ValidRowCount, inserted.InvalidRowCount, inserted.PreviewRowsJson, inserted.ErrorCode, inserted.RequestedByUserId, inserted.CreatedAtUtc, inserted.PreviewCompletedAtUtc, inserted.ProcessedRowCount, inserted.SucceededRowCount, inserted.ExecutionFailedRowCount, inserted.NextLineNumber, inserted.ExecutionRowsJson, inserted.ErrorReceiptFileId, inserted.ExecutionStartedAtUtc, inserted.ExecutionCompletedAtUtc, inserted.LeaseId, inserted.LeaseExpiresAtUtc, inserted.Version, inserted.ColumnMappingJson, inserted.ActorSessionBindingJson
        """;

    /// <summary>可领取条件：排队中，或执行中但租约已到期/缺失（升级前崩溃行）。</summary>
    private const string ClaimablePredicate = """
        (StatusKey = 'queued'
         OR (StatusKey = 'executing'
             AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc <= @Now)))
        AND SourceReleaseStartedAtUtc IS NULL AND SourceReleasedAtUtc IS NULL
        """;

    /// <summary>Worker 调度目录仅返回有可领取任务的租户标识，不授予读取任务或文件的权限。</summary>
    public static readonly SqlStatement ListPendingTenantIdsSqlServer = new(
        "import_export.task.pending_tenants.sqlserver",
        $"""
        SELECT TOP (@BatchSize) TenantId
        FROM fn_import_export_task
        WHERE {ClaimablePredicate}
          AND (@AfterTenantId IS NULL OR TenantId > @AfterTenantId)
        GROUP BY TenantId
        ORDER BY TenantId
        """, SqlDataScope.Global);

    /// <summary>MySQL Worker 调度目录；具体任务必须在可信租户上下文建立后领取。</summary>
    public static readonly SqlStatement ListPendingTenantIdsMySql = new(
        "import_export.task.pending_tenants.mysql",
        $"""
        SELECT TenantId
        FROM fn_import_export_task
        WHERE {ClaimablePredicate}
          AND (@AfterTenantId IS NULL OR TenantId > @AfterTenantId)
        GROUP BY TenantId
        ORDER BY TenantId
        LIMIT @BatchSize
        """, SqlDataScope.Global);

    public static readonly SqlStatement Insert = new(
        "import_export.task.insert",
        $"""
        INSERT INTO fn_import_export_task
            ({SelectColumns})
        VALUES
            (@Id, @TenantId, @SchemaKey, @SchemaDisplayName, @WorksheetKey, @SourceFileId, @SourceFileName, @SourceExpiresAtUtc, NULL, NULL,
             @StatusKey, @TotalRows, @ValidRowCount, @InvalidRowCount, @PreviewRowsJson, @ErrorCode,
             @RequestedByUserId, @CreatedAtUtc, @PreviewCompletedAtUtc,
             @ProcessedRowCount, @SucceededRowCount, @ExecutionFailedRowCount, @NextLineNumber,
             @ExecutionRowsJson, @ErrorReceiptFileId, @ExecutionStartedAtUtc, @ExecutionCompletedAtUtc,
             @LeaseId, @LeaseExpiresAtUtc, @Version, @ColumnMappingJson, NULL)
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

    /// <summary>只有原上传者的当前租户任务参与取消判断；无正文投影也避免从其他任务泄露状态。</summary>
    public static readonly SqlStatement FindOwnedCancellationState = new("import_export.task.cancel_state", """
        SELECT Id,StatusKey,Version FROM fn_import_export_task
        WHERE TenantId=@TenantId AND RequestedByUserId=@UserId AND Id=@Id
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    /// <summary>与 Worker 领取竞争同一行；版本或状态先被推进时返回零，不撤销可能已提交的业务行。</summary>
    public static readonly SqlStatement CancelOwnedPending = new("import_export.task.cancel_pending", """
        UPDATE fn_import_export_task SET StatusKey='canceled',ExecutionCompletedAtUtc=@Now,
            LeaseId=NULL,LeaseExpiresAtUtc=NULL,Version=Version+1
        WHERE TenantId=@TenantId AND RequestedByUserId=@UserId AND Id=@Id AND Version=@Version
            AND StatusKey IN ('preview_succeeded','queued','execution_partial') AND LeaseId IS NULL
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement PageSqlServer = new(
        "import_export.task.page.sqlserver",
        $"""
        SELECT COUNT(1)
        FROM fn_import_export_task
        WHERE TenantId = @TenantId AND (@SchemaKey IS NULL OR SchemaKey = @SchemaKey)
          AND (@StatusKey IS NULL OR StatusKey = @StatusKey);

        SELECT {SelectColumns}
        FROM fn_import_export_task
        WHERE TenantId = @TenantId AND (@SchemaKey IS NULL OR SchemaKey = @SchemaKey)
          AND (@StatusKey IS NULL OR StatusKey = @StatusKey)
        ORDER BY CreatedAtUtc DESC, Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement PageMySql = new(
        "import_export.task.page.mysql",
        $"""
        SELECT COUNT(1)
        FROM fn_import_export_task
        WHERE TenantId = @TenantId AND (@SchemaKey IS NULL OR SchemaKey = @SchemaKey)
          AND (@StatusKey IS NULL OR StatusKey = @StatusKey);

        SELECT {SelectColumns}
        FROM fn_import_export_task
        WHERE TenantId = @TenantId AND (@SchemaKey IS NULL OR SchemaKey = @SchemaKey)
          AND (@StatusKey IS NULL OR StatusKey = @StatusKey)
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
            ActorSessionBindingJson = @ActorSessionBindingJson,
            ErrorReceiptFileId = @ErrorReceiptFileId,
            ExecutionStartedAtUtc = @ExecutionStartedAtUtc,
            ExecutionCompletedAtUtc = @ExecutionCompletedAtUtc,
            ErrorCode = @ErrorCode,
            LeaseId = NULL,
            LeaseExpiresAtUtc = NULL,
            Version = Version + 1
        WHERE TenantId = @TenantId AND Id = @Id
          AND StatusKey = @ExpectedStatusKey AND Version = @ExpectedVersion
          AND SourceExpiresAtUtc > @SourceNow AND SourceReleaseStartedAtUtc IS NULL AND SourceReleasedAtUtc IS NULL
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    /// <summary>双库共用的取消 CAS；版本防止 queued→executing→queued 后覆盖新的检查点。</summary>
    public static readonly SqlStatement Cancel = new(
        "import_export.task.cancel",
        """
        UPDATE fn_import_export_task
        SET StatusKey = 'cancelled', ExecutionCompletedAtUtc = @ExecutionCompletedAtUtc,
            LeaseId = NULL, LeaseExpiresAtUtc = NULL, Version = Version + 1
        WHERE TenantId = @TenantId AND Id = @Id AND RequestedByUserId = @RequestedByUserId
          AND StatusKey = @ExpectedStatusKey AND Version = @ExpectedVersion
          AND StatusKey IN ('preview_succeeded', 'queued', 'execution_partial', 'execution_failed')
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

    /// <summary>检查点和失败回写共用本租户任务行锁，不把等待锁前的时间当成仍持有租约。</summary>
    public static readonly SqlStatement LockExecutionSqlServer = new(
        "import_export.task.lock_execution.sqlserver",
        $"""
        SELECT {SelectColumns} FROM fn_import_export_task WITH (UPDLOCK, ROWLOCK)
        WHERE TenantId = @TenantId AND Id = @Id
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement LockExecutionMySql = new(
        "import_export.task.lock_execution.mysql",
        $"""
        SELECT {SelectColumns} FROM fn_import_export_task
        WHERE TenantId = @TenantId AND Id = @Id FOR UPDATE
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

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
          AND LeaseId = @LeaseId AND LeaseExpiresAtUtc > @NowUtc
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
          AND LeaseId = @LeaseId AND LeaseExpiresAtUtc > @NowUtc
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement RenewExecutionLease = new(
        "import_export.task.renew_execution_lease",
        """
        UPDATE fn_import_export_task
        SET LeaseExpiresAtUtc = @LeaseExpiresAtUtc
        WHERE TenantId = @TenantId AND Id = @Id AND StatusKey = 'executing'
          AND LeaseId = @LeaseId AND LeaseExpiresAtUtc > @NowUtc
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    // 首/后续页分别使用固定 SQL，让时间边界直接成为查询范围；不读取正文或会话 JSON。
    public static readonly SqlStatement MineFirstSqlServer = new("import_export.task.mine.first.sqlserver", """
        SELECT TOP (@Take) Id, TenantId, SchemaKey, SchemaDisplayName, WorksheetKey, SourceFileId, SourceFileName, SourceExpiresAtUtc, SourceReleaseStartedAtUtc, SourceReleasedAtUtc, StatusKey, TotalRows, ValidRowCount, InvalidRowCount, ErrorCode, RequestedByUserId, CreatedAtUtc, PreviewCompletedAtUtc, ProcessedRowCount, SucceededRowCount, ExecutionFailedRowCount, NextLineNumber, ErrorReceiptFileId, ExecutionStartedAtUtc, ExecutionCompletedAtUtc, Version
        FROM fn_import_export_task
        WHERE TenantId=@TenantId AND RequestedByUserId=@RequestedByUserId
            AND (@StatusKey IS NULL OR StatusKey=@StatusKey)
        ORDER BY CreatedAtUtc DESC,Id DESC
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement MineAfterSqlServer = new("import_export.task.mine.after.sqlserver", """
        SELECT TOP (@Take) Id, TenantId, SchemaKey, SchemaDisplayName, WorksheetKey, SourceFileId, SourceFileName, SourceExpiresAtUtc, SourceReleaseStartedAtUtc, SourceReleasedAtUtc, StatusKey, TotalRows, ValidRowCount, InvalidRowCount, ErrorCode, RequestedByUserId, CreatedAtUtc, PreviewCompletedAtUtc, ProcessedRowCount, SucceededRowCount, ExecutionFailedRowCount, NextLineNumber, ErrorReceiptFileId, ExecutionStartedAtUtc, ExecutionCompletedAtUtc, Version
        FROM fn_import_export_task
        WHERE TenantId=@TenantId AND RequestedByUserId=@RequestedByUserId
            AND (@StatusKey IS NULL OR StatusKey=@StatusKey)
            AND CreatedAtUtc<=@BeforeCreatedAtUtc AND (CreatedAtUtc<@BeforeCreatedAtUtc OR Id<@BeforeId)
        ORDER BY CreatedAtUtc DESC,Id DESC
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement MineFirstMySql = new("import_export.task.mine.first.mysql", """
        SELECT Id, TenantId, SchemaKey, SchemaDisplayName, WorksheetKey, SourceFileId, SourceFileName, SourceExpiresAtUtc, SourceReleaseStartedAtUtc, SourceReleasedAtUtc, StatusKey, TotalRows, ValidRowCount, InvalidRowCount, ErrorCode, RequestedByUserId, CreatedAtUtc, PreviewCompletedAtUtc, ProcessedRowCount, SucceededRowCount, ExecutionFailedRowCount, NextLineNumber, ErrorReceiptFileId, ExecutionStartedAtUtc, ExecutionCompletedAtUtc, Version
        FROM fn_import_export_task
        WHERE TenantId=@TenantId AND RequestedByUserId=@RequestedByUserId
            AND (@StatusKey IS NULL OR StatusKey=@StatusKey)
        ORDER BY CreatedAtUtc DESC,Id DESC
        LIMIT @Take
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement MineAfterMySql = new("import_export.task.mine.after.mysql", """
        SELECT Id, TenantId, SchemaKey, SchemaDisplayName, WorksheetKey, SourceFileId, SourceFileName, SourceExpiresAtUtc, SourceReleaseStartedAtUtc, SourceReleasedAtUtc, StatusKey, TotalRows, ValidRowCount, InvalidRowCount, ErrorCode, RequestedByUserId, CreatedAtUtc, PreviewCompletedAtUtc, ProcessedRowCount, SucceededRowCount, ExecutionFailedRowCount, NextLineNumber, ErrorReceiptFileId, ExecutionStartedAtUtc, ExecutionCompletedAtUtc, Version
        FROM fn_import_export_task
        WHERE TenantId=@TenantId AND RequestedByUserId=@RequestedByUserId
            AND (@StatusKey IS NULL OR StatusKey=@StatusKey)
            AND CreatedAtUtc<=@BeforeCreatedAtUtc AND (CreatedAtUtc<@BeforeCreatedAtUtc OR Id<@BeforeId)
        ORDER BY CreatedAtUtc DESC,Id DESC
        LIMIT @Take
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
