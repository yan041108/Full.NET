using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.ImportExport.Persistence;

/// <summary>静态 Schema 导入任务 SQL 语句。</summary>
internal static class ImportExportTaskSql
{
    public static readonly SqlStatement Insert = new(
        "import_export.task.insert",
        """
        INSERT INTO fn_import_export_task
            (Id, TenantId, SchemaKey, SchemaDisplayName, WorksheetKey, SourceFileId, SourceFileName,
             StatusKey, TotalRows, ValidRowCount, InvalidRowCount, PreviewRowsJson, ErrorCode,
             RequestedByUserId, CreatedAtUtc, PreviewCompletedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @SchemaKey, @SchemaDisplayName, @WorksheetKey, @SourceFileId, @SourceFileName,
             @StatusKey, @TotalRows, @ValidRowCount, @InvalidRowCount, @PreviewRowsJson, @ErrorCode,
             @RequestedByUserId, @CreatedAtUtc, @PreviewCompletedAtUtc, @Version)
        """,
        SqlDataScope.TenantRequired);

    public static readonly SqlStatement FindById = new(
        "import_export.task.find_by_id",
        """
        SELECT Id, TenantId, SchemaKey, SchemaDisplayName, WorksheetKey, SourceFileId, SourceFileName,
               StatusKey, TotalRows, ValidRowCount, InvalidRowCount, PreviewRowsJson, ErrorCode,
               RequestedByUserId, CreatedAtUtc, PreviewCompletedAtUtc, Version
        FROM fn_import_export_task
        WHERE Id = @Id
        """,
        SqlDataScope.TenantRequired);

    public static readonly SqlStatement PageSqlServer = new(
        "import_export.task.page.sqlserver",
        """
        SELECT COUNT(1)
        FROM fn_import_export_task
        WHERE (@SchemaKey IS NULL OR SchemaKey = @SchemaKey);

        SELECT Id, TenantId, SchemaKey, SchemaDisplayName, WorksheetKey, SourceFileId, SourceFileName,
               StatusKey, TotalRows, ValidRowCount, InvalidRowCount, PreviewRowsJson, ErrorCode,
               RequestedByUserId, CreatedAtUtc, PreviewCompletedAtUtc, Version
        FROM fn_import_export_task
        WHERE (@SchemaKey IS NULL OR SchemaKey = @SchemaKey)
        ORDER BY CreatedAtUtc DESC, Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
        """,
        SqlDataScope.TenantRequired);

    public static readonly SqlStatement PageMySql = new(
        "import_export.task.page.mysql",
        """
        SELECT COUNT(1)
        FROM fn_import_export_task
        WHERE (@SchemaKey IS NULL OR SchemaKey = @SchemaKey);

        SELECT Id, TenantId, SchemaKey, SchemaDisplayName, WorksheetKey, SourceFileId, SourceFileName,
               StatusKey, TotalRows, ValidRowCount, InvalidRowCount, PreviewRowsJson, ErrorCode,
               RequestedByUserId, CreatedAtUtc, PreviewCompletedAtUtc, Version
        FROM fn_import_export_task
        WHERE (@SchemaKey IS NULL OR SchemaKey = @SchemaKey)
        ORDER BY CreatedAtUtc DESC, Id DESC
        LIMIT @PageSize OFFSET @Offset;
        """,
        SqlDataScope.TenantRequired);
}
