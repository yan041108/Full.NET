-- 111：为工作流待办固化发布版本中的超时、催办和升级调度状态。
-- information_schema 门禁保证重复执行和部分 DDL 恢复安全，历史待办保持未配置超时。
DROP PROCEDURE IF EXISTS add_workflow_todo_timeout_columns;
DELIMITER $$
CREATE PROCEDURE add_workflow_todo_timeout_columns()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_todo' AND COLUMN_NAME = 'DueAtUtc') THEN
        ALTER TABLE fn_workflow_todo ADD DueAtUtc datetime(6) NULL COMMENT '待办逾期时间(UTC)';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_todo' AND COLUMN_NAME = 'NextReminderAtUtc') THEN
        ALTER TABLE fn_workflow_todo ADD NextReminderAtUtc datetime(6) NULL COMMENT '下一催办时间(UTC)';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_todo' AND COLUMN_NAME = 'EscalateAtUtc') THEN
        ALTER TABLE fn_workflow_todo ADD EscalateAtUtc datetime(6) NULL COMMENT '升级通知时间(UTC)';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_todo' AND COLUMN_NAME = 'MaxReminderCount') THEN
        ALTER TABLE fn_workflow_todo ADD MaxReminderCount int NOT NULL DEFAULT 0 COMMENT '最大催办次数';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_todo' AND COLUMN_NAME = 'ReminderIntervalMinutes') THEN
        ALTER TABLE fn_workflow_todo ADD ReminderIntervalMinutes int NOT NULL DEFAULT 0 COMMENT '催办间隔分钟数';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_todo' AND COLUMN_NAME = 'ReminderCount') THEN
        ALTER TABLE fn_workflow_todo ADD ReminderCount int NOT NULL DEFAULT 0 COMMENT '已发送催办次数';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_todo' AND COLUMN_NAME = 'EscalationRecipientUserId') THEN
        ALTER TABLE fn_workflow_todo ADD EscalationRecipientUserId BINARY(16) NULL COMMENT '固定升级通知接收人标识';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_todo' AND COLUMN_NAME = 'LastReminderAtUtc') THEN
        ALTER TABLE fn_workflow_todo ADD LastReminderAtUtc datetime(6) NULL COMMENT '最后催办时间(UTC)';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_todo' AND COLUMN_NAME = 'EscalatedAtUtc') THEN
        ALTER TABLE fn_workflow_todo ADD EscalatedAtUtc datetime(6) NULL COMMENT '已升级时间(UTC)';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_todo' AND COLUMN_NAME = 'NextTimeoutSignalAtUtc') THEN
        ALTER TABLE fn_workflow_todo ADD NextTimeoutSignalAtUtc datetime(6) NULL COMMENT '下一超时信号时间(UTC)';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_todo' AND CONSTRAINT_NAME = 'CK_fn_workflow_todo_ReminderCounts') THEN
        ALTER TABLE fn_workflow_todo ADD CONSTRAINT CK_fn_workflow_todo_ReminderCounts
            CHECK (MaxReminderCount >= 0 AND ReminderCount >= 0 AND ReminderCount <= MaxReminderCount);
    END IF;
END$$
DELIMITER ;
CALL add_workflow_todo_timeout_columns();
DROP PROCEDURE add_workflow_todo_timeout_columns;

SET @hasTimeoutScan := (SELECT COUNT(1) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_workflow_todo' AND INDEX_NAME = 'IX_fn_workflow_todo_TimeoutScan');
SET @addTimeoutScan := IF(@hasTimeoutScan = 0, 'ALTER TABLE fn_workflow_todo ADD INDEX IX_fn_workflow_todo_TimeoutScan (StatusKey, NextTimeoutSignalAtUtc, Id)', 'SELECT 1');
PREPARE stmt FROM @addTimeoutScan;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
