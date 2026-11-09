-- 只汇总阻塞切版的任务，不返回租户、任务或文件信息。
SET NOCOUNT ON;
SELECT CASE WHEN DATABASEPROPERTYEX(DB_NAME(), 'Updateability') = 'READ_WRITE'
    THEN (SELECT COUNT_BIG(*) FROM dbo.fn_reporting_export_task
        WHERE CONCAT(StatusKey, '#') COLLATE Latin1_General_100_BIN2 NOT IN ('succeeded#', 'failed#', 'canceled#')
           OR LeaseId IS NOT NULL OR LeaseExpiresAtUtc IS NOT NULL)
    ELSE -1 END;
