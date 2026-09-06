-- 142：Host 公告已读回执表，支撑收件箱与发布方阅读统计。

CREATE TABLE IF NOT EXISTS fn_notifications_announcement_read_receipt
(
    Id BINARY(16) NOT NULL,
    AnnouncementId BINARY(16) NOT NULL,
    UserId BINARY(16) NOT NULL,
    ReadAtUtc DATETIME(6) NOT NULL,
    PRIMARY KEY (Id),
    UNIQUE KEY UQ_fn_notifications_announcement_read_receipt_AnnouncementUser (AnnouncementId, UserId),
    CONSTRAINT FK_fn_notifications_announcement_read_receipt_Announcement
        FOREIGN KEY (AnnouncementId) REFERENCES fn_notifications_announcement(Id),
    KEY IX_fn_notifications_announcement_read_receipt_User_ReadAtUtc (UserId, ReadAtUtc, AnnouncementId),
    KEY IX_fn_notifications_announcement_read_receipt_Announcement_ReadAtUtc (AnnouncementId, ReadAtUtc, UserId)
);
