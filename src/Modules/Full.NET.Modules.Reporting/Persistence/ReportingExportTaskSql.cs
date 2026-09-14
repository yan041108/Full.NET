using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Reporting.Persistence;

/// <summary>报表导出任务 SQL 语句。</summary>
internal static class ReportingExportTaskSql
{
    private const string SelectColumns = """
        Id, TenantId, DefinitionId, VersionNumber, DefinitionKey, DefinitionName, FormatKey,
        ParametersJson, StatusKey, OutputFileId, OutputFileName, RowCount, ErrorCode, ErrorMessage,
        RequestedByUserId, CreatedAtUtc, CompletedAtUtc, LeaseId, LeaseExpiresAtUtc,
        ActorPermissionCodesJson, Version
        """;

    private const string SelectColumnsSqlServer = """
        Id, TenantId, DefinitionId, VersionNumber, DefinitionKey, DefinitionName, FormatKey,
        ParametersJson, StatusKey, OutputFileId, OutputFileName, [RowCount], ErrorCode, ErrorMessage,
        RequestedByUserId, CreatedAtUtc, CompletedAtUtc, LeaseId, LeaseExpiresAtUtc,
        ActorPermissionCodesJson, Version
        """;

    private const string InsertedColumnsSqlServer = """
        inserted.Id, inserted.TenantId, inserted.DefinitionId, inserted.VersionNumber, inserted.DefinitionKey, inserted.DefinitionName, inserted.FormatKey, inserted.ParametersJson, inserted.StatusKey, inserted.OutputFileId, inserted.OutputFileName, inserted.[RowCount], inserted.ErrorCode, inserted.ErrorMessage, inserted.RequestedByUserId, inserted.CreatedAtUtc, inserted.CompletedAtUtc, inserted.LeaseId, inserted.LeaseExpiresAtUtc, inserted.ActorPermissionCodesJson, inserted.Version
        """;

    /// <summary>可领取条件：排队中，或处理中但租约已到期/缺失。</summary>
    private const string ClaimablePredicate = """
        (StatusKey = 'queued'
         OR (StatusKey = 'processing'
             AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc <= @Now)))
        """;

    /// <summary>Worker 调度目录只返回有可领取导出任务的租户。</summary>
    public static readonly SqlStatement ListPendingTenantIdsSqlServer = new(
        "reporting.export_task.pending_tenants.sqlserver",
        $"""
        SELECT TOP (@BatchSize) TenantId
        FROM fn_reporting_export_task
        WHERE {ClaimablePredicate}
        GROUP BY TenantId
        ORDER BY MIN(CreatedAtUtc), TenantId
        """, SqlDataScope.Global);

    /// <summary>MySQL Worker 调度目录。</summary>
    public static readonly SqlStatement ListPendingTenantIdsMySql = new(
        "reporting.export_task.pending_tenants.mysql",
        $"""
        SELECT TenantId
        FROM fn_reporting_export_task
        WHERE {ClaimablePredicate}
        GROUP BY TenantId
        ORDER BY MIN(CreatedAtUtc), TenantId
        LIMIT @BatchSize
        """, SqlDataScope.Global);

    public static readonly SqlStatement Insert = new(
        "reporting.export_task.insert",
        $"""
        INSERT INTO fn_reporting_export_task
            ({SelectColumns})
        VALUES
            (@Id, @TenantId, @DefinitionId, @VersionNumber, @DefinitionKey, @DefinitionName, @FormatKey,
             @ParametersJson, @StatusKey, @OutputFileId, @OutputFileName, @RowCount, @ErrorCode, @ErrorMessage,
             @RequestedByUserId, @CreatedAtUtc, @CompletedAtUtc, @LeaseId, @LeaseExpiresAtUtc,
             @ActorPermissionCodesJson, @Version)
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement InsertSqlServer = new(
        "reporting.export_task.insert.sqlserver",
        $"""
        INSERT INTO fn_reporting_export_task
            ({SelectColumnsSqlServer})
        VALUES
            (@Id, @TenantId, @DefinitionId, @VersionNumber, @DefinitionKey, @DefinitionName, @FormatKey,
             @ParametersJson, @StatusKey, @OutputFileId, @OutputFileName, @RowCount, @ErrorCode, @ErrorMessage,
             @RequestedByUserId, @CreatedAtUtc, @CompletedAtUtc, @LeaseId, @LeaseExpiresAtUtc,
             @ActorPermissionCodesJson, @Version)
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindById = new(
        "reporting.export_task.find_by_id",
        $"""
        SELECT {SelectColumns}
        FROM fn_reporting_export_task
        WHERE TenantId = @TenantId AND Id = @Id
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindByIdSqlServer = new(
        "reporting.export_task.find_by_id.sqlserver",
        $"""
        SELECT {SelectColumnsSqlServer}
        FROM fn_reporting_export_task
        WHERE TenantId = @TenantId AND Id = @Id
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement PageSqlServer = new(
        "reporting.export_task.page.sqlserver",
        $"""
        SELECT COUNT(1)
        FROM fn_reporting_export_task
        WHERE TenantId = @TenantId AND (@DefinitionId IS NULL OR DefinitionId = @DefinitionId);

        SELECT {SelectColumnsSqlServer}
        FROM fn_reporting_export_task
        WHERE TenantId = @TenantId AND (@DefinitionId IS NULL OR DefinitionId = @DefinitionId)
        ORDER BY CreatedAtUtc DESC, Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement PageMySql = new(
        "reporting.export_task.page.mysql",
        $"""
        SELECT COUNT(1)
        FROM fn_reporting_export_task
        WHERE TenantId = @TenantId AND (@DefinitionId IS NULL OR DefinitionId = @DefinitionId);

        SELECT {SelectColumns}
        FROM fn_reporting_export_task
        WHERE TenantId = @TenantId AND (@DefinitionId IS NULL OR DefinitionId = @DefinitionId)
        ORDER BY CreatedAtUtc DESC, Id DESC
        LIMIT @PageSize OFFSET @Offset;
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ClaimQueuedSqlServer = new(
        "reporting.export_task.claim.sqlserver",
        $"""
        WITH candidates AS (
            SELECT TOP (1) Id
            FROM fn_reporting_export_task WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE TenantId = @TenantId AND {ClaimablePredicate}
            ORDER BY CreatedAtUtc, Id
        )
        UPDATE task
        SET StatusKey = 'processing',
            LeaseId = @LeaseId,
            LeaseExpiresAtUtc = @LeaseExpiresAtUtc,
            Version = task.Version + 1
        OUTPUT {InsertedColumnsSqlServer}
        FROM fn_reporting_export_task AS task
        INNER JOIN candidates ON candidates.Id = task.Id
        WHERE task.TenantId = @TenantId;
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ClaimByIdSqlServer = new(
        "reporting.export_task.claim_by_id.sqlserver",
        $"""
        UPDATE fn_reporting_export_task
        SET StatusKey = 'processing',
            LeaseId = @LeaseId,
            LeaseExpiresAtUtc = @LeaseExpiresAtUtc,
            Version = Version + 1
        OUTPUT {InsertedColumnsSqlServer}
        WHERE TenantId = @TenantId AND Id = @Id
          AND {ClaimablePredicate};
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement SelectClaimableIdsMySql = new(
        "reporting.export_task.select_claimable_ids.mysql",
        $"""
        SELECT Id
        FROM fn_reporting_export_task
        WHERE TenantId = @TenantId AND {ClaimablePredicate}
        ORDER BY CreatedAtUtc, Id
        LIMIT 1
        FOR UPDATE SKIP LOCKED
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement SelectClaimableIdByIdMySql = new(
        "reporting.export_task.select_claimable_id_by_id.mysql",
        $"""
        SELECT Id
        FROM fn_reporting_export_task
        WHERE TenantId = @TenantId AND Id = @Id AND {ClaimablePredicate}
        LIMIT 1
        FOR UPDATE SKIP LOCKED
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ClaimByIdsMySql = new(
        "reporting.export_task.claim_by_ids.mysql",
        $"""
        UPDATE fn_reporting_export_task
        SET StatusKey = 'processing',
            LeaseId = @LeaseId,
            LeaseExpiresAtUtc = @LeaseExpiresAtUtc,
            Version = Version + 1
        WHERE TenantId = @TenantId AND {ClaimablePredicate}
          AND Id IN @Ids
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement SelectByIds = new(
        "reporting.export_task.select_by_ids",
        $"""
        SELECT {SelectColumns}
        FROM fn_reporting_export_task
        WHERE TenantId = @TenantId AND Id IN @Ids
        ORDER BY CreatedAtUtc, Id
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement SelectByIdsSqlServer = new(
        "reporting.export_task.select_by_ids.sqlserver",
        $"""
        SELECT {SelectColumnsSqlServer}
        FROM fn_reporting_export_task
        WHERE TenantId = @TenantId AND Id IN @Ids
        ORDER BY CreatedAtUtc, Id
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    /// <summary>按数据库提供程序返回 Insert 语句，SQL Server 需转义保留列名 RowCount。</summary>
    public static SqlStatement InsertFor(DatabaseProvider provider) =>
        provider == DatabaseProvider.SqlServer ? InsertSqlServer : Insert;

    /// <summary>按数据库提供程序返回按 Id 查询语句。</summary>
    public static SqlStatement FindByIdFor(DatabaseProvider provider) =>
        provider == DatabaseProvider.SqlServer ? FindByIdSqlServer : FindById;

    /// <summary>按数据库提供程序返回批量 Id 查询语句。</summary>
    public static SqlStatement SelectByIdsFor(DatabaseProvider provider) =>
        provider == DatabaseProvider.SqlServer ? SelectByIdsSqlServer : SelectByIds;

    public static readonly SqlStatement CompleteSucceeded = new(
        "reporting.export_task.complete_succeeded",
        """
        UPDATE fn_reporting_export_task
        SET StatusKey = @StatusKey,
            OutputFileId = @OutputFileId,
            OutputFileName = @OutputFileName,
            RowCount = @RowCount,
            ErrorCode = NULL,
            ErrorMessage = NULL,
            CompletedAtUtc = @CompletedAtUtc,
            LeaseId = NULL,
            LeaseExpiresAtUtc = NULL,
            Version = Version + 1
        WHERE TenantId = @TenantId AND Id = @Id
          AND StatusKey = 'processing'
          AND LeaseId = @LeaseId
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement CompleteSucceededSqlServer = new(
        "reporting.export_task.complete_succeeded.sqlserver",
        """
        UPDATE fn_reporting_export_task
        SET StatusKey = @StatusKey,
            OutputFileId = @OutputFileId,
            OutputFileName = @OutputFileName,
            [RowCount] = @RowCount,
            ErrorCode = NULL,
            ErrorMessage = NULL,
            CompletedAtUtc = @CompletedAtUtc,
            LeaseId = NULL,
            LeaseExpiresAtUtc = NULL,
            Version = Version + 1
        WHERE TenantId = @TenantId AND Id = @Id
          AND StatusKey = 'processing'
          AND LeaseId = @LeaseId
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement CompleteFailed = new(
        "reporting.export_task.complete_failed",
        """
        UPDATE fn_reporting_export_task
        SET StatusKey = @StatusKey,
            RowCount = @RowCount,
            ErrorCode = @ErrorCode,
            ErrorMessage = @ErrorMessage,
            CompletedAtUtc = @CompletedAtUtc,
            LeaseId = NULL,
            LeaseExpiresAtUtc = NULL,
            Version = Version + 1
        WHERE TenantId = @TenantId AND Id = @Id
          AND StatusKey = 'processing'
          AND LeaseId = @LeaseId
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement CompleteFailedSqlServer = new(
        "reporting.export_task.complete_failed.sqlserver",
        """
        UPDATE fn_reporting_export_task
        SET StatusKey = @StatusKey,
            [RowCount] = @RowCount,
            ErrorCode = @ErrorCode,
            ErrorMessage = @ErrorMessage,
            CompletedAtUtc = @CompletedAtUtc,
            LeaseId = NULL,
            LeaseExpiresAtUtc = NULL,
            Version = Version + 1
        WHERE TenantId = @TenantId AND Id = @Id
          AND StatusKey = 'processing'
          AND LeaseId = @LeaseId
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
