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

    CREATE INDEX IX_fn_notifications_inbox_message_RecipientCreatedAtUtc
        ON dbo.fn_notifications_inbox_message(RecipientUserId, CreatedAtUtc DESC, Id);

    CREATE INDEX IX_fn_notifications_inbox_message_RecipientUnread
        ON dbo.fn_notifications_inbox_message(RecipientUserId, Status, Id)
        WHERE Status = 'unread';
END;
