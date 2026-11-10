-- 只核对一个明确的租户任务。文件名、对象存储键和凭据不进入证据输出。
SET NOCOUNT ON;
DECLARE @TenantId uniqueidentifier = '{{TENANT_ID}}', @TaskId uniqueidentifier = '{{TASK_ID}}';
SELECT CAST(COALESCE(NULLIF((
    SELECT t.TenantId AS tenantId, t.Id AS taskId, t.StatusKey AS statusKey,
        t.OutputFileId AS outputFileId, t.[RowCount] AS recordedRowCount,
        CAST(CASE WHEN t.LeaseId IS NOT NULL OR t.LeaseExpiresAtUtc IS NOT NULL THEN 1 ELSE 0 END AS bit) AS hasLease,
        counts.total AS fileCount, counts.ready AS readyFileCount, counts.pending AS pendingFileCount,
        latest.Id AS latestReadyFileId, latest.SizeBytes AS latestReadyBytes, latest.ContentHash AS latestReadySha256,
        CAST(CASE WHEN latest.Id IS NULL THEN NULL WHEN LEFT(latest.OriginalFileName, 15) COLLATE Latin1_General_100_BIN2 = N'reporting-rows-' THEN 1 ELSE 0 END AS bit) AS hasRowIdentityPrefix
    FROM dbo.fn_reporting_export_task AS t
    CROSS APPLY (
        SELECT COUNT_BIG(*) AS total, COUNT_BIG(CASE WHEN f.StatusKey = 'ready' THEN 1 END) AS ready,
            COUNT_BIG(CASE WHEN f.StatusKey = 'pending' THEN 1 END) AS pending
        FROM dbo.fn_files_tenant_resource_file AS f
        WHERE f.TenantId = @TenantId AND f.OwnerModuleKey = 'reporting' AND f.ResourceId = @TaskId
    ) AS counts
    OUTER APPLY (
        SELECT TOP (1) f.Id, f.SizeBytes, f.ContentHash, f.OriginalFileName
        FROM dbo.fn_files_tenant_resource_file AS f
        WHERE f.TenantId = @TenantId AND f.OwnerModuleKey = 'reporting' AND f.ResourceId = @TaskId AND f.StatusKey = 'ready'
        ORDER BY f.CreatedAtUtc DESC, f.Id DESC
    ) AS latest
    WHERE t.TenantId = @TenantId AND t.Id = @TaskId
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES
), N''), N'null') AS nvarchar(2000));
