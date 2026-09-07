using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Reporting.Persistence;

/// <summary>报表导出任务 SQL 语句。</summary>
internal static class ReportingExportTaskSql
{
    private const string SelectColumns = """
        Id, TenantId, DefinitionId, VersionNumber, DefinitionKey, DefinitionName, FormatKey,
        ParametersJson, StatusKey, OutputFileId, OutputFileName, RowCount, ErrorCode, ErrorMessage,
        RequestedByUserId, CreatedAtUtc, CompletedAtUtc, Version
        """;

    public static readonly SqlStatement Insert = new(
        "reporting.export_task.insert",
        $"""
        INSERT INTO fn_reporting_export_task
            ({SelectColumns})
        VALUES
            (@Id, @TenantId, @DefinitionId, @VersionNumber, @DefinitionKey, @DefinitionName, @FormatKey,
             @ParametersJson, @StatusKey, @OutputFileId, @OutputFileName, @RowCount, @ErrorCode, @ErrorMessage,
             @RequestedByUserId, @CreatedAtUtc, @CompletedAtUtc, @Version)
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

    public static readonly SqlStatement PageSqlServer = new(
        "reporting.export_task.page.sqlserver",
        $"""
        SELECT COUNT(1)
        FROM fn_reporting_export_task
        WHERE TenantId = @TenantId AND (@DefinitionId IS NULL OR DefinitionId = @DefinitionId);

        SELECT {SelectColumns}
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
            Version = Version + 1
        WHERE TenantId = @TenantId AND Id = @Id
          AND Version = @Version
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
            Version = Version + 1
        WHERE TenantId = @TenantId AND Id = @Id
          AND Version = @Version
        """,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
