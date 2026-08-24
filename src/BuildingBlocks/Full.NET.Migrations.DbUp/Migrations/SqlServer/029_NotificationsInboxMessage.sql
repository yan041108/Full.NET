-- 029：用户站内信收件箱。

IF OBJECT_ID(N'dbo.fn_notifications_inbox_message', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_inbox_message
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        RecipientUserId uniqueidentifier NOT NULL,
        Title nvarchar(200) NOT NULL,
        Content nvarchar(4000) NOT NULL,
        Status varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ReadAtUtc datetimeoffset(7) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CreatedByUserId uniqueidentifier NULL,
        CONSTRAINT PK_fn_notifications_inbox_message PRIMARY KEY CLUSTERED (Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知收件箱消息表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_inbox_message';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_inbox_message'), N'Content', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'内容', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_inbox_message', @level2type=N'COLUMN', @level2name=N'Content';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_inbox_message'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_inbox_message', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_inbox_message'), N'CreatedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_inbox_message', @level2type=N'COLUMN', @level2name=N'CreatedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_inbox_message'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_inbox_message', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_inbox_message'), N'ReadAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已读时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_inbox_message', @level2type=N'COLUMN', @level2name=N'ReadAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_inbox_message'), N'RecipientUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'接收人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_inbox_message', @level2type=N'COLUMN', @level2name=N'RecipientUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_inbox_message'), N'Status', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_inbox_message', @level2type=N'COLUMN', @level2name=N'Status';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_inbox_message'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_inbox_message', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_inbox_message'), N'Title', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'标题', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_inbox_message', @level2type=N'COLUMN', @level2name=N'Title';

    CREATE INDEX IX_fn_notifications_inbox_message_RecipientCreatedAtUtc
        ON dbo.fn_notifications_inbox_message(RecipientUserId, CreatedAtUtc DESC, Id);

    CREATE INDEX IX_fn_notifications_inbox_message_RecipientUnread
        ON dbo.fn_notifications_inbox_message(RecipientUserId, Status, Id)
        WHERE Status = 'unread';
END;
