-- 169：Host 文档 Office 预览转换任务表。

IF OBJECT_ID(N'dbo.fn_document_preview_task', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_document_preview_task
    (
        Id uniqueidentifier NOT NULL,
        DocumentItemId uniqueidentifier NOT NULL,
        VersionId uniqueidentifier NULL,
        DocumentTitle nvarchar(256) NOT NULL,
        SourceFileId uniqueidentifier NOT NULL,
        SourceFileName nvarchar(260) NULL,
        SourceMimeType varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
        OutputFileId uniqueidentifier NULL,
        StatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ProviderKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ErrorCode varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
        RequestedByUserId uniqueidentifier NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        StartedAtUtc datetimeoffset(7) NULL,
        CompletedAtUtc datetimeoffset(7) NULL,
        Version bigint NOT NULL,
        CONSTRAINT PK_fn_document_preview_task PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_fn_document_preview_task_Item
            FOREIGN KEY (DocumentItemId) REFERENCES dbo.fn_document_item(Id),
        CONSTRAINT CK_fn_document_preview_task_StatusKey
            CHECK (StatusKey IN (N'pending', N'processing', N'succeeded', N'failed'))
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_preview_task')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档预览任务表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_preview_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_preview_task'), N'CompletedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完成时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task', @level2type=N'COLUMN', @level2name=N'CompletedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_preview_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_preview_task'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_preview_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_preview_task'), N'DocumentItemId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档项标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task', @level2type=N'COLUMN', @level2name=N'DocumentItemId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_preview_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_preview_task'), N'DocumentTitle', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档标题', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task', @level2type=N'COLUMN', @level2name=N'DocumentTitle';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_preview_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_preview_task'), N'ErrorCode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'错误码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task', @level2type=N'COLUMN', @level2name=N'ErrorCode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_preview_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_preview_task'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_preview_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_preview_task'), N'OutputFileId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'输出文件标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task', @level2type=N'COLUMN', @level2name=N'OutputFileId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_preview_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_preview_task'), N'ProviderKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'存储提供程序键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task', @level2type=N'COLUMN', @level2name=N'ProviderKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_preview_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_preview_task'), N'RequestedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'请求人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task', @level2type=N'COLUMN', @level2name=N'RequestedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_preview_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_preview_task'), N'SourceFileId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'源文件标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task', @level2type=N'COLUMN', @level2name=N'SourceFileId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_preview_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_preview_task'), N'SourceFileName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'源文件名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task', @level2type=N'COLUMN', @level2name=N'SourceFileName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_preview_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_preview_task'), N'SourceMimeType', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'源 MIME 类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task', @level2type=N'COLUMN', @level2name=N'SourceMimeType';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_preview_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_preview_task'), N'StartedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'开始时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task', @level2type=N'COLUMN', @level2name=N'StartedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_preview_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_preview_task'), N'StatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task', @level2type=N'COLUMN', @level2name=N'StatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_preview_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_preview_task'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task', @level2type=N'COLUMN', @level2name=N'Version';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_preview_task')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_preview_task'), N'VersionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task', @level2type=N'COLUMN', @level2name=N'VersionId';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_preview_task')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Host 文档 Office 预览转换任务表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_preview_task';
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_document_preview_task')
      AND indexObject.name = N'IX_fn_document_preview_task_StatusKey_CreatedAtUtc'
)
    CREATE INDEX IX_fn_document_preview_task_StatusKey_CreatedAtUtc
        ON dbo.fn_document_preview_task(StatusKey, CreatedAtUtc, Id);

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_document_preview_task')
      AND indexObject.name = N'IX_fn_document_preview_task_DocumentItemId'
)
    CREATE INDEX IX_fn_document_preview_task_DocumentItemId
        ON dbo.fn_document_preview_task(DocumentItemId, CreatedAtUtc DESC);
