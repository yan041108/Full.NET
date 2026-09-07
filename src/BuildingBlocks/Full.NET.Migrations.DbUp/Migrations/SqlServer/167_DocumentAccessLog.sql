-- 167：文档访问日志表。

IF OBJECT_ID(N'dbo.fn_document_access_log', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_document_access_log
    (
        Id uniqueidentifier NOT NULL,
        DocumentItemId uniqueidentifier NOT NULL,
        DocumentTitle nvarchar(256) NOT NULL,
        AccessTypeKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        SourceKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ActorUserId uniqueidentifier NULL,
        OccurredAtUtc datetimeoffset(7) NOT NULL,
        ClientIpFingerprint varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        CONSTRAINT PK_fn_document_access_log PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_fn_document_access_log_Item
            FOREIGN KEY (DocumentItemId) REFERENCES dbo.fn_document_item(Id),
        CONSTRAINT CK_fn_document_access_log_AccessTypeKey
            CHECK (AccessTypeKey IN (N'download', N'preview', N'share_access')),
        CONSTRAINT CK_fn_document_access_log_SourceKey
            CHECK (SourceKey IN (N'authenticated', N'share'))
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_access_log')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档访问日志表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_access_log';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_access_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_access_log'), N'AccessTypeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'访问类型键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_access_log', @level2type=N'COLUMN', @level2name=N'AccessTypeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_access_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_access_log'), N'ActorUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作者用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_access_log', @level2type=N'COLUMN', @level2name=N'ActorUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_access_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_access_log'), N'ClientIpFingerprint', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'客户端 IP 指纹', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_access_log', @level2type=N'COLUMN', @level2name=N'ClientIpFingerprint';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_access_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_access_log'), N'DocumentItemId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档项标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_access_log', @level2type=N'COLUMN', @level2name=N'DocumentItemId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_access_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_access_log'), N'DocumentTitle', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档标题', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_access_log', @level2type=N'COLUMN', @level2name=N'DocumentTitle';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_access_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_access_log'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_access_log', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_access_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_access_log'), N'OccurredAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发生时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_access_log', @level2type=N'COLUMN', @level2name=N'OccurredAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_access_log')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_access_log'), N'SourceKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'来源键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_access_log', @level2type=N'COLUMN', @level2name=N'SourceKey';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_access_log')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档访问日志表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_access_log';
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_document_access_log')
      AND indexObject.name = N'IX_fn_document_access_log_OccurredAtUtc_Id'
)
    CREATE INDEX IX_fn_document_access_log_OccurredAtUtc_Id
        ON dbo.fn_document_access_log(OccurredAtUtc DESC, Id DESC);

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_document_access_log')
      AND indexObject.name = N'IX_fn_document_access_log_DocumentItemId'
)
    CREATE INDEX IX_fn_document_access_log_DocumentItemId
        ON dbo.fn_document_access_log(DocumentItemId, OccurredAtUtc DESC);
