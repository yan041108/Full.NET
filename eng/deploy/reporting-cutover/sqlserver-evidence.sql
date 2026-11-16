-- 只核对一个明确的租户任务。文件名、对象存储键和凭据不进入证据输出。
SET NOCOUNT ON;
DECLARE @TenantId uniqueidentifier = '{{TENANT_ID}}', @TaskId uniqueidentifier = '{{TASK_ID}}';
SELECT CAST(COALESCE(NULLIF((
    SELECT @TenantId AS tenantId, @TaskId AS taskId,
        CAST(CASE WHEN t.Id IS NULL THEN 0 ELSE 1 END AS bit) AS taskExists, t.StatusKey AS statusKey,
        t.OutputFileId AS outputFileId, t.[RowCount] AS recordedRowCount,
        CAST(CASE WHEN t.LeaseId IS NOT NULL OR t.LeaseExpiresAtUtc IS NOT NULL THEN 1 ELSE 0 END AS bit) AS hasLease,
        counts.total AS fileCount, counts.ready AS readyFileCount, counts.pending AS pendingFileCount,
        counts.pending + counts.ready - CASE WHEN EXISTS (
            SELECT 1 FROM dbo.fn_files_tenant_resource_file AS f
            WHERE f.Id = t.OutputFileId AND f.TenantId = @TenantId AND f.OwnerModuleKey = 'reporting'
                AND f.ResourceId = @TaskId AND f.StatusKey = 'ready'
        ) THEN 1 ELSE 0 END AS unresolvedFileCount,
        latest.Id AS latestReadyFileId, latest.SizeBytes AS latestReadyBytes, latest.ContentHash AS latestReadySha256,
        CAST(CASE WHEN latest.Id IS NULL THEN NULL WHEN LEFT(latest.OriginalFileName, 15) COLLATE Latin1_General_100_BIN2 = N'reporting-rows-' THEN 1 ELSE 0 END AS bit) AS hasRowIdentityPrefix
    FROM (
        SELECT COUNT_BIG(*) AS total, COUNT_BIG(CASE WHEN f.StatusKey = 'ready' THEN 1 END) AS ready,
            COUNT_BIG(CASE WHEN f.StatusKey = 'pending' THEN 1 END) AS pending
        FROM dbo.fn_files_tenant_resource_file AS f
        WHERE f.TenantId = @TenantId AND f.OwnerModuleKey = 'reporting' AND f.ResourceId = @TaskId
    ) AS counts
    LEFT JOIN dbo.fn_reporting_export_task AS t ON t.TenantId = @TenantId AND t.Id = @TaskId
    OUTER APPLY (
        SELECT TOP (1) f.Id, f.SizeBytes, f.ContentHash, f.OriginalFileName
        FROM dbo.fn_files_tenant_resource_file AS f
        WHERE f.TenantId = @TenantId AND f.OwnerModuleKey = 'reporting' AND f.ResourceId = @TaskId AND f.StatusKey = 'ready'
        ORDER BY f.CreatedAtUtc DESC, f.Id DESC
    ) AS latest
    WHERE t.Id IS NOT NULL OR counts.total > 0
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES
), N''), N'null') AS nvarchar(2000));
