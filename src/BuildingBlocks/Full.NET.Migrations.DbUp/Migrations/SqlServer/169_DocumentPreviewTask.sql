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
