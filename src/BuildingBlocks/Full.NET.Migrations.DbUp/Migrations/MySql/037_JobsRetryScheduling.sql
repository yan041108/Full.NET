-- 037：MySQL DDL 会隐式提交，使用静态分支收敛列与索引的半完成状态。

DROP PROCEDURE IF EXISTS fn_jobs_retry_scheduling;
DELIMITER $$
CREATE PROCEDURE fn_jobs_retry_scheduling()
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_jobs_execution'
          AND COLUMN_NAME = 'NextAttemptAtUtc'
    ) THEN
        ALTER TABLE fn_jobs_execution
            ADD COLUMN NextAttemptAtUtc datetime(6) NULL;
    END IF;

    IF EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_jobs_execution'
          AND INDEX_NAME = 'IX_fn_jobs_execution_PendingLease'
    ) THEN
        DROP INDEX IX_fn_jobs_execution_PendingLease
            ON fn_jobs_execution;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_jobs_execution'
          AND INDEX_NAME = 'IX_fn_jobs_execution_PendingNextAttemptLease'
    ) THEN
        CREATE INDEX IX_fn_jobs_execution_PendingNextAttemptLease
            ON fn_jobs_execution
                (Status, NextAttemptAtUtc, LeaseExpiresAtUtc, CreatedAtUtc);
    END IF;
END$$
DELIMITER ;

CALL fn_jobs_retry_scheduling();
DROP PROCEDURE fn_jobs_retry_scheduling;
