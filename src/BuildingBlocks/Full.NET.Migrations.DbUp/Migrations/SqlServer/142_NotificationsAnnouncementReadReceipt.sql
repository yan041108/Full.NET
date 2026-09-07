-- 142：Host 公告已读回执表，支撑收件箱与发布方阅读统计。

IF OBJECT_ID(N'dbo.fn_notifications_announcement_read_receipt', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_announcement_read_receipt
    (
        Id uniqueidentifier NOT NULL,
        AnnouncementId uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        ReadAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_notifications_announcement_read_receipt PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_fn_notifications_announcement_read_receipt_AnnouncementUser
            UNIQUE (AnnouncementId, UserId),
        CONSTRAINT FK_fn_notifications_announcement_read_receipt_Announcement
            FOREIGN KEY (AnnouncementId) REFERENCES dbo.fn_notifications_announcement(Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_announcement_read_receipt')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知公告已读回执表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_announcement_read_receipt';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_announcement_read_receipt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_announcement_read_receipt'), N'AnnouncementId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'公告标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_announcement_read_receipt', @level2type=N'COLUMN', @level2name=N'AnnouncementId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_announcement_read_receipt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_announcement_read_receipt'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_announcement_read_receipt', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_announcement_read_receipt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_announcement_read_receipt'), N'ReadAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已读时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_announcement_read_receipt', @level2type=N'COLUMN', @level2name=N'ReadAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_announcement_read_receipt')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_announcement_read_receipt'), N'UserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_announcement_read_receipt', @level2type=N'COLUMN', @level2name=N'UserId';

    CREATE INDEX IX_fn_notifications_announcement_read_receipt_User_ReadAtUtc
        ON dbo.fn_notifications_announcement_read_receipt(UserId, ReadAtUtc DESC, AnnouncementId);

    CREATE INDEX IX_fn_notifications_announcement_read_receipt_Announcement_ReadAtUtc
        ON dbo.fn_notifications_announcement_read_receipt(AnnouncementId, ReadAtUtc DESC, UserId);
END;
