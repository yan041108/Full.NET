-- 037：为显式可重试任务增加数据库到期领取边界。

IF COL_LENGTH(N'dbo.fn_jobs_execution', N'NextAttemptAtUtc') IS NULL
BEGIN
    ALTER TABLE dbo.fn_jobs_execution
        ADD NextAttemptAtUtc datetimeoffset(7) NULL;
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'下次重试时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_execution', @level2type=N'COLUMN', @level2name=N'NextAttemptAtUtc';
END;

IF EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_jobs_execution')
      AND name = N'IX_fn_jobs_execution_PendingLease'
)
BEGIN
    DROP INDEX IX_fn_jobs_execution_PendingLease
        ON dbo.fn_jobs_execution;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_jobs_execution')
      AND name = N'IX_fn_jobs_execution_PendingNextAttemptLease'
)
BEGIN
    CREATE INDEX IX_fn_jobs_execution_PendingNextAttemptLease
        ON dbo.fn_jobs_execution
            (Status, NextAttemptAtUtc, LeaseExpiresAtUtc, CreatedAtUtc)
        WHERE Status = 'pending';
END;
