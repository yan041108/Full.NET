-- 同一租户报表任务或其未处置文件只计一次，也保留无原任务可匹配的文件阻断。
SET SESSION lock_wait_timeout = 15;
WITH blocking_resources AS (
    SELECT TenantId, Id AS ResourceId
    FROM fn_reporting_export_task
    WHERE StatusKey IS NULL OR CAST(StatusKey AS BINARY) NOT IN ('succeeded', 'failed', 'canceled')
       OR LeaseId IS NOT NULL OR LeaseExpiresAtUtc IS NOT NULL
    UNION
    SELECT f.TenantId, f.ResourceId
    FROM fn_files_tenant_resource_file AS f
    WHERE f.OwnerModuleKey = 'reporting'
      AND (
          f.StatusKey = 'pending'
          OR (f.StatusKey = 'ready' AND NOT EXISTS (
              SELECT 1 FROM fn_reporting_export_task AS t
              WHERE t.TenantId = f.TenantId AND t.Id = f.ResourceId AND t.OutputFileId = f.Id
          ))
      )
)
-- Result projection
SELECT /*+ MAX_EXECUTION_TIME(15000) */ CASE WHEN @@global.read_only = 0 AND @@global.super_read_only = 0
    THEN (SELECT COUNT(*) FROM blocking_resources)
    ELSE -1 END;
