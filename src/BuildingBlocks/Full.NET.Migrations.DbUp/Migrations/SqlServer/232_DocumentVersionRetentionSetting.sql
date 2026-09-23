-- 232：Host 文档版本保留策略数据库覆盖（单行，TenantId IS NULL）。

IF OBJECT_ID(N'dbo.fn_document_version_retention_setting', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_document_version_retention_setting
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        MinimumRetainedVersionsPerItem int NOT NULL,
        MaximumRetainedHistoryVersions int NOT NULL,
        PollSeconds int NOT NULL,
        BatchSize int NOT NULL,
        Version bigint NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_document_version_retention_setting PRIMARY KEY NONCLUSTERED (Id)
    );

    CREATE UNIQUE INDEX UX_fn_document_version_retention_setting_Host
        ON dbo.fn_document_version_retention_setting (TenantId)
        WHERE TenantId IS NULL;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version_retention_setting')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档version retention setting表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_retention_setting';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version_retention_setting')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version_retention_setting'), N'BatchSize', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Batch Size', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_retention_setting', @level2type=N'COLUMN', @level2name=N'BatchSize';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version_retention_setting')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version_retention_setting'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_retention_setting', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version_retention_setting')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version_retention_setting'), N'MaximumRetainedHistoryVersions', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Maximum Retained History Versions', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_retention_setting', @level2type=N'COLUMN', @level2name=N'MaximumRetainedHistoryVersions';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version_retention_setting')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version_retention_setting'), N'MinimumRetainedVersionsPerItem', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Minimum Retained Versions Per Item', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_retention_setting', @level2type=N'COLUMN', @level2name=N'MinimumRetainedVersionsPerItem';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version_retention_setting')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version_retention_setting'), N'PollSeconds', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Poll Seconds', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_retention_setting', @level2type=N'COLUMN', @level2name=N'PollSeconds';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version_retention_setting')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version_retention_setting'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_retention_setting', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version_retention_setting')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version_retention_setting'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_retention_setting', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_version_retention_setting')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_version_retention_setting'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_retention_setting', @level2type=N'COLUMN', @level2name=N'Version';
END;
