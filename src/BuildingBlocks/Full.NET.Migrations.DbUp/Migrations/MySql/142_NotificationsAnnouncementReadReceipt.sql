-- 142：Host 公告已读回执表，支撑收件箱与发布方阅读统计。

CREATE TABLE IF NOT EXISTS fn_notifications_announcement_read_receipt (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    AnnouncementId BINARY(16) NOT NULL COMMENT '公告标识',
    UserId BINARY(16) NOT NULL COMMENT '用户标识',
    ReadAtUtc DATETIME(6) NOT NULL COMMENT '已读时间(UTC)',
    PRIMARY KEY (Id),
    UNIQUE KEY UQ_fn_notifications_announcement_read_receipt_AnnouncementUser (AnnouncementId, UserId),
    CONSTRAINT FK_fn_notifications_announcement_read_receipt_Announcement
        FOREIGN KEY (AnnouncementId) REFERENCES fn_notifications_announcement(Id),
    KEY IX_fn_notifications_announcement_read_receipt_User_ReadAtUtc (UserId, ReadAtUtc, AnnouncementId),
    KEY IX_fn_notif_ann_read_rcpt_Announcement_ReadAt (AnnouncementId, ReadAtUtc, UserId)
) COMMENT='通知公告已读回执表';
