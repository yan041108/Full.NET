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
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'导入导出任务表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'ErrorCode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'错误码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'ErrorCode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'InvalidRowCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'无效行数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'InvalidRowCount';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'PreviewCompletedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Preview Completed At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'PreviewCompletedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'PreviewRowsJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Preview Rows(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'PreviewRowsJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'RequestedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'请求人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'RequestedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'SchemaDisplayName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'结构显示名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'SchemaDisplayName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'SchemaKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'结构键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'SchemaKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'SourceFileId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'源文件标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'SourceFileId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'SourceFileName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'源文件名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'SourceFileName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'StatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'StatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'TotalRows', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'总行数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'TotalRows';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'ValidRowCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'有效行数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'ValidRowCount';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'Version';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_import_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_import_export_task'), N'WorksheetKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作表键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_import_export_task', @level2type=N'COLUMN', @level2name=N'WorksheetKey';

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
