-- 归档只影响 Host 管理列表，保留公告业务状态和历史回执。
DROP PROCEDURE IF EXISTS fn_notifications_announcement_archive;
DELIMITER $$
CREATE PROCEDURE fn_notifications_announcement_archive()
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_notifications_announcement'
          AND COLUMN_NAME = 'ArchivedAtUtc'
    ) THEN
        ALTER TABLE fn_notifications_announcement
            ADD COLUMN ArchivedAtUtc datetime(6) NULL COMMENT '公告移出日常管理列表的时间(UTC)；NULL 表示未归档';
    END IF;
END$$
DELIMITER ;
CALL fn_notifications_announcement_archive();
DROP PROCEDURE IF EXISTS fn_notifications_announcement_archive;
