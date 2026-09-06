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
