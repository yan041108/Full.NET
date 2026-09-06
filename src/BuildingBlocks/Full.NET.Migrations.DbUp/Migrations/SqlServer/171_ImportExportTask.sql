-- 171：ImportExport 静态 Schema 导入任务表。

IF OBJECT_ID(N'dbo.fn_import_export_task', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_import_export_task
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        SchemaKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        SchemaDisplayName nvarchar(128) NOT NULL,
        WorksheetKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        SourceFileId uniqueidentifier NOT NULL,
        SourceFileName nvarchar(260) NULL,
        StatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        TotalRows int NOT NULL,
        ValidRowCount int NOT NULL,
        InvalidRowCount int NOT NULL,
        PreviewRowsJson nvarchar(max) NULL,
        ErrorCode varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
        RequestedByUserId uniqueidentifier NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        PreviewCompletedAtUtc datetimeoffset(7) NULL,
        Version bigint NOT NULL,
        CONSTRAINT PK_fn_import_export_task PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_import_export_task_StatusKey
            CHECK (StatusKey IN (N'uploaded', N'preview_succeeded', N'preview_failed'))
    );

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'ImportExport 静态 Schema 导入任务表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task';
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_import_export_task')
      AND indexObject.name = N'IX_fn_import_export_task_TenantId_CreatedAtUtc'
)
    CREATE INDEX IX_fn_import_export_task_TenantId_CreatedAtUtc
        ON dbo.fn_import_export_task(TenantId, CreatedAtUtc DESC, Id);

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_import_export_task')
      AND indexObject.name = N'IX_fn_import_export_task_SchemaKey'
)
    CREATE INDEX IX_fn_import_export_task_SchemaKey
        ON dbo.fn_import_export_task(TenantId, SchemaKey, CreatedAtUtc DESC);
