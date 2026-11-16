-- 同一租户报表任务或其未处置文件只计一次，也保留无原任务可匹配的文件阻断。
SET NOCOUNT ON;
SELECT CASE WHEN DATABASEPROPERTYEX(DB_NAME(), 'Updateability') = 'READ_WRITE'
    THEN (
        SELECT COUNT_BIG(*)
        FROM (
            SELECT TenantId, Id AS ResourceId
            FROM dbo.fn_reporting_export_task
            WHERE CONCAT(StatusKey, '#') COLLATE Latin1_General_100_BIN2 NOT IN ('succeeded#', 'failed#', 'canceled#')
               OR LeaseId IS NOT NULL OR LeaseExpiresAtUtc IS NOT NULL
            UNION
            SELECT f.TenantId, f.ResourceId
            FROM dbo.fn_files_tenant_resource_file AS f
            WHERE f.OwnerModuleKey = 'reporting'
              AND (
                  f.StatusKey = 'pending'
                  OR (f.StatusKey = 'ready' AND NOT EXISTS (
                      SELECT 1 FROM dbo.fn_reporting_export_task AS t
                      WHERE t.TenantId = f.TenantId AND t.Id = f.ResourceId AND t.OutputFileId = f.Id
                  ))
              )
        ) AS blocking_resources
    )
    ELSE -1 END;
