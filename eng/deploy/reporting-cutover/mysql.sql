-- 只汇总阻塞切版的任务，不返回租户、任务或文件信息。
SET SESSION lock_wait_timeout = 15;
SELECT /*+ MAX_EXECUTION_TIME(15000) */ CASE WHEN @@global.read_only = 0 AND @@global.super_read_only = 0
    THEN (SELECT COUNT(*) FROM fn_reporting_export_task
        WHERE StatusKey IS NULL OR CAST(StatusKey AS BINARY) NOT IN ('succeeded', 'failed', 'canceled')
           OR LeaseId IS NOT NULL OR LeaseExpiresAtUtc IS NOT NULL)
    ELSE -1 END;
