-- 144：通知 Intent 邮件附件投影表，供 Files claim 探测与 Worker 有界装载。

IF OBJECT_ID(N'dbo.fn_notifications_intent_attachment', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_intent_attachment
    (
        Id uniqueidentifier NOT NULL,
        IntentId uniqueidentifier NOT NULL,
        FileId uniqueidentifier NOT NULL,
        SortOrder int NOT NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_notifications_intent_attachment PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_fn_notifications_intent_attachment_Intent
            FOREIGN KEY (IntentId) REFERENCES dbo.fn_notifications_intent(Id),
        CONSTRAINT UQ_fn_notifications_intent_attachment_IntentFile
            UNIQUE (IntentId, FileId),
        CONSTRAINT CK_fn_notifications_intent_attachment_SortOrder CHECK (SortOrder >= 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_intent_attachment')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知意图附件表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_intent_attachment';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_intent_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_intent_attachment'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_intent_attachment', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_intent_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_intent_attachment'), N'FileId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文件标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_intent_attachment', @level2type=N'COLUMN', @level2name=N'FileId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_intent_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_intent_attachment'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_intent_attachment', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_intent_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_intent_attachment'), N'IntentId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知意图标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_intent_attachment', @level2type=N'COLUMN', @level2name=N'IntentId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_intent_attachment')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_intent_attachment'), N'SortOrder', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'排序顺序', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_intent_attachment', @level2type=N'COLUMN', @level2name=N'SortOrder';

    CREATE CLUSTERED INDEX IX_fn_notifications_intent_attachment_IntentId
        ON dbo.fn_notifications_intent_attachment (IntentId);

    CREATE NONCLUSTERED INDEX IX_fn_notifications_intent_attachment_FileId
        ON dbo.fn_notifications_intent_attachment (FileId);

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_intent_attachment')
          AND minor_id = 0
          AND name = N'MS_Description')
        EXEC sys.sp_addextendedproperty
            @name = N'MS_Description',
            @value = N'通知意图邮件附件投影表',
            @level0type = N'SCHEMA', @level0name = N'dbo',
            @level1type = N'TABLE', @level1name = N'fn_notifications_intent_attachment';
END
