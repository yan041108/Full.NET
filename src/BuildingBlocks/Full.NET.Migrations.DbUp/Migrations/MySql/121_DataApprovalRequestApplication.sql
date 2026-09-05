-- 121：DataApproval 请求批准后业务应用结果与恢复元数据。
DROP PROCEDURE IF EXISTS fn_dataapproval_request_application_extension;
DELIMITER $$
CREATE PROCEDURE fn_dataapproval_request_application_extension()
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_dataapproval_request'
          AND COLUMN_NAME = 'ApplicationStatusKey') THEN
        ALTER TABLE fn_dataapproval_request
            ADD COLUMN ApplicationStatusKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'none' COMMENT '业务应用状态键';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_dataapproval_request'
          AND COLUMN_NAME = 'LastApplicationFailureCode') THEN
        ALTER TABLE fn_dataapproval_request
            ADD COLUMN LastApplicationFailureCode varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '最近应用失败稳定错误码';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_dataapproval_request'
          AND COLUMN_NAME = 'LastApplicationFailureMessage') THEN
        ALTER TABLE fn_dataapproval_request
            ADD COLUMN LastApplicationFailureMessage varchar(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NULL COMMENT '最近应用失败说明';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_dataapproval_request'
          AND COLUMN_NAME = 'LastApplicationAttemptAtUtc') THEN
        ALTER TABLE fn_dataapproval_request
            ADD COLUMN LastApplicationAttemptAtUtc datetime(6) NULL COMMENT '最近应用尝试时间(UTC)';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_dataapproval_request'
          AND COLUMN_NAME = 'ApplicationAttemptCount') THEN
        ALTER TABLE fn_dataapproval_request
            ADD COLUMN ApplicationAttemptCount int NOT NULL DEFAULT 0 COMMENT '应用尝试次数';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_dataapproval_request'
          AND CONSTRAINT_NAME = 'CK_fn_dataapproval_request_ApplicationStatusKey') THEN
        ALTER TABLE fn_dataapproval_request
            ADD CONSTRAINT CK_fn_dataapproval_request_ApplicationStatusKey
            CHECK (ApplicationStatusKey IN ('none', 'pending_apply', 'applied', 'failed_retryable', 'failed_terminal'));
    END IF;

    SET @application_index_exists := (
        SELECT COUNT(1)
        FROM information_schema.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_dataapproval_request'
          AND INDEX_NAME = 'IX_fn_dataapproval_request_PendingApplication');
    SET @ddl := IF(
        @application_index_exists = 0,
        'CREATE INDEX IX_fn_dataapproval_request_PendingApplication ON fn_dataapproval_request (StatusKey, ApplicationStatusKey, LastApplicationAttemptAtUtc, SubmittedAtUtc)',
        'SELECT 1');
    PREPARE stmt FROM @ddl;
    EXECUTE stmt;
    DEALLOCATE PREPARE stmt;
END$$
DELIMITER ;
CALL fn_dataapproval_request_application_extension();
DROP PROCEDURE IF EXISTS fn_dataapproval_request_application_extension;
