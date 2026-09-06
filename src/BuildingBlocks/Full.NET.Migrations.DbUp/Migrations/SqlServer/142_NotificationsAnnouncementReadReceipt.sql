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

    CREATE INDEX IX_fn_notifications_announcement_read_receipt_User_ReadAtUtc
        ON dbo.fn_notifications_announcement_read_receipt(UserId, ReadAtUtc DESC, AnnouncementId);

    CREATE INDEX IX_fn_notifications_announcement_read_receipt_Announcement_ReadAtUtc
        ON dbo.fn_notifications_announcement_read_receipt(AnnouncementId, ReadAtUtc DESC, UserId);
END;
