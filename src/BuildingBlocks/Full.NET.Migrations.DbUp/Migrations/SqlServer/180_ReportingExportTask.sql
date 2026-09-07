-- 180：Reporting 导出任务表。

IF OBJECT_ID(N'dbo.fn_reporting_export_task', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_reporting_export_task
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        DefinitionId uniqueidentifier NOT NULL,
        VersionNumber int NOT NULL,
        DefinitionKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DefinitionName nvarchar(128) NOT NULL,
        FormatKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ParametersJson nvarchar(max) NOT NULL,
        StatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        OutputFileId uniqueidentifier NULL,
        OutputFileName nvarchar(260) NULL,
        RowCount int NOT NULL
            CONSTRAINT DF_fn_reporting_export_task_RowCount DEFAULT (0),
        ErrorCode varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
        ErrorMessage nvarchar(1024) NULL,
        RequestedByUserId uniqueidentifier NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CompletedAtUtc datetimeoffset(7) NULL,
        Version bigint NOT NULL
            CONSTRAINT DF_fn_reporting_export_task_Version DEFAULT (1),
        CONSTRAINT PK_fn_reporting_export_task PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_reporting_export_task_StatusKey
            CHECK (StatusKey IN (N'processing', N'succeeded', N'failed')),
        CONSTRAINT CK_fn_reporting_export_task_FormatKey
            CHECK (FormatKey IN (N'excel')),
        CONSTRAINT CK_fn_reporting_export_task_VersionNumber CHECK (VersionNumber > 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'报表导出任务表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_export_task';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_export_task'), N'CompletedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完成时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_export_task', @level2type=N'COLUMN', @level2name=N'CompletedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_export_task'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_export_task', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_export_task'), N'DefinitionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'定义标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_export_task', @level2type=N'COLUMN', @level2name=N'DefinitionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_export_task'), N'DefinitionKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'定义稳定键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_export_task', @level2type=N'COLUMN', @level2name=N'DefinitionKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_export_task'), N'DefinitionName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'定义名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_export_task', @level2type=N'COLUMN', @level2name=N'DefinitionName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_export_task'), N'ErrorCode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'错误码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_export_task', @level2type=N'COLUMN', @level2name=N'ErrorCode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_export_task'), N'ErrorMessage', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'错误消息', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_export_task', @level2type=N'COLUMN', @level2name=N'ErrorMessage';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_export_task'), N'FormatKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'格式键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_export_task', @level2type=N'COLUMN', @level2name=N'FormatKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_export_task'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_export_task', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_export_task'), N'OutputFileId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'输出文件标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_export_task', @level2type=N'COLUMN', @level2name=N'OutputFileId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_export_task'), N'OutputFileName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'输出文件名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_export_task', @level2type=N'COLUMN', @level2name=N'OutputFileName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_export_task'), N'ParametersJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Parameters(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_export_task', @level2type=N'COLUMN', @level2name=N'ParametersJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_export_task'), N'RequestedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'请求人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_export_task', @level2type=N'COLUMN', @level2name=N'RequestedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_export_task'), N'RowCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'行数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_export_task', @level2type=N'COLUMN', @level2name=N'RowCount';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_export_task'), N'StatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_export_task', @level2type=N'COLUMN', @level2name=N'StatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_export_task'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_export_task', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_export_task'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_export_task', @level2type=N'COLUMN', @level2name=N'Version';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_export_task'), N'VersionNumber', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_reporting_export_task', @level2type=N'COLUMN', @level2name=N'VersionNumber';
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
      AND name = N'IX_fn_reporting_export_task_TenantId_CreatedAtUtc')
    CREATE INDEX IX_fn_reporting_export_task_TenantId_CreatedAtUtc
        ON dbo.fn_reporting_export_task(TenantId, CreatedAtUtc DESC, Id);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
      AND name = N'IX_fn_reporting_export_task_DefinitionId')
    CREATE INDEX IX_fn_reporting_export_task_DefinitionId
        ON dbo.fn_reporting_export_task(TenantId, DefinitionId, CreatedAtUtc DESC);
