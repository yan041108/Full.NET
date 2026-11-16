-- 同一租户报表任务或其未处置文件只计一次，也保留无原任务可匹配的文件阻断。
SET SESSION lock_wait_timeout = 15;
WITH task_reasons AS (
    -- 按原始字节比较使大小写变化及尾空格仍按未知状态阻断。
    SELECT TenantId, Id AS ResourceId,
        CASE WHEN StatusKey IS NULL OR CAST(StatusKey AS BINARY) NOT IN ('succeeded', 'failed', 'canceled') THEN 1 ELSE 0 END AS HasUnfinishedTask,
        CASE WHEN LeaseId IS NOT NULL OR LeaseExpiresAtUtc IS NOT NULL THEN 1 ELSE 0 END AS HasTaskLease
    FROM fn_reporting_export_task
), resource_reasons AS (
    SELECT TenantId, ResourceId, HasUnfinishedTask, HasTaskLease, 0 AS HasPendingUpload, 0 AS HasUnboundReadyFile
    FROM task_reasons
    WHERE HasUnfinishedTask = 1 OR HasTaskLease = 1
    UNION ALL
    SELECT f.TenantId, f.ResourceId, 0, 0,
        CASE WHEN f.StatusKey = 'pending' THEN 1 ELSE 0 END,
        CASE WHEN f.StatusKey = 'ready' THEN 1 ELSE 0 END
    FROM fn_files_tenant_resource_file AS f
    WHERE f.OwnerModuleKey = 'reporting'
      AND (
          f.StatusKey = 'pending'
          OR (f.StatusKey = 'ready' AND NOT EXISTS (
              SELECT 1 FROM fn_reporting_export_task AS t
              WHERE t.TenantId = f.TenantId AND t.Id = f.ResourceId AND t.OutputFileId = f.Id
          ))
      )
), blocking_resources AS (
    -- 计数与分页共用去重后的资源，保留同一资源下同时存在的原因。
    SELECT TenantId, ResourceId, MAX(HasUnfinishedTask) AS HasUnfinishedTask, MAX(HasTaskLease) AS HasTaskLease,
        MAX(HasPendingUpload) AS HasPendingUpload, MAX(HasUnboundReadyFile) AS HasUnboundReadyFile
    FROM resource_reasons
    GROUP BY TenantId, ResourceId
)
-- Result projection
SELECT /*+ MAX_EXECUTION_TIME(15000) */ CASE WHEN @@global.read_only = 0 AND @@global.super_read_only = 0
    THEN (SELECT COUNT(*) FROM blocking_resources)
    ELSE -1 END;
