-- 120：DataApproval 请求工作流关联恢复元数据。
DROP PROCEDURE IF EXISTS fn_dataapproval_request_recovery_extension;
DELIMITER $$
CREATE PROCEDURE fn_dataapproval_request_recovery_extension()
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_dataapproval_request'
          AND COLUMN_NAME = 'RecoveryStatusKey') THEN
        ALTER TABLE fn_dataapproval_request
            ADD COLUMN RecoveryStatusKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'none' COMMENT '恢复状态键';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_dataapproval_request'
          AND COLUMN_NAME = 'LastFailureCode') THEN
        ALTER TABLE fn_dataapproval_request
            ADD COLUMN LastFailureCode varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '最近失败稳定错误码';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_dataapproval_request'
          AND COLUMN_NAME = 'LastFailureMessage') THEN
        ALTER TABLE fn_dataapproval_request
            ADD COLUMN LastFailureMessage varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT '最近失败说明';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_dataapproval_request'
          AND COLUMN_NAME = 'LastRecoveryAttemptAtUtc') THEN
        ALTER TABLE fn_dataapproval_request
            ADD COLUMN LastRecoveryAttemptAtUtc datetime(6) NULL COMMENT '最近恢复尝试时间(UTC)';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_dataapproval_request'
          AND COLUMN_NAME = 'RecoveryAttemptCount') THEN
        ALTER TABLE fn_dataapproval_request
            ADD COLUMN RecoveryAttemptCount int NOT NULL DEFAULT 0 COMMENT '恢复尝试次数';
    END IF;

    UPDATE fn_dataapproval_request
    SET RecoveryStatusKey = 'pending_link'
    WHERE StatusKey = 'pending'
      AND WorkflowInstanceId IS NULL
      AND RecoveryStatusKey = 'none';

    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_dataapproval_request'
          AND CONSTRAINT_NAME = 'CK_fn_dataapproval_request_RecoveryStatusKey') THEN
        ALTER TABLE fn_dataapproval_request
            ADD CONSTRAINT CK_fn_dataapproval_request_RecoveryStatusKey
            CHECK (RecoveryStatusKey IN ('none', 'pending_link', 'failed_retryable', 'failed_terminal'));
    END IF;

    SET @recovery_index_exists := (
        SELECT COUNT(1)
        FROM information_schema.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_dataapproval_request'
          AND INDEX_NAME = 'IX_fn_dataapproval_request_PendingRecovery');
    SET @ddl := IF(
        @recovery_index_exists = 0,
        'CREATE INDEX IX_fn_dataapproval_request_PendingRecovery ON fn_dataapproval_request (StatusKey, RecoveryStatusKey, LastRecoveryAttemptAtUtc, SubmittedAtUtc)',
        'SELECT 1');
    PREPARE stmt FROM @ddl;
    EXECUTE stmt;
    DEALLOCATE PREPARE stmt;
END$$
DELIMITER ;
CALL fn_dataapproval_request_recovery_extension();
DROP PROCEDURE IF EXISTS fn_dataapproval_request_recovery_extension;
