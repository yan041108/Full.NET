-- UUID 使用正式 Binary16 布局。与 Files 的恢复读取采用相同的就绪文件排序。
SET SESSION lock_wait_timeout = 15;
SET @TenantId = UUID_TO_BIN('{{TENANT_ID}}', 0), @TaskId = UUID_TO_BIN('{{TASK_ID}}', 0);
SELECT /*+ MAX_EXECUTION_TIME(15000) */ COALESCE((
    SELECT JSON_OBJECT('tenantId', BIN_TO_UUID(t.TenantId, 0), 'taskId', BIN_TO_UUID(t.Id, 0), 'statusKey', t.StatusKey,
        'outputFileId', BIN_TO_UUID(t.OutputFileId, 0), 'recordedRowCount', t.RowCount,
        'hasLease', IF(t.LeaseId IS NOT NULL OR t.LeaseExpiresAtUtc IS NOT NULL, TRUE, FALSE),
        'fileCount', counts.total, 'readyFileCount', counts.ready, 'pendingFileCount', counts.pending,
        'latestReadyFileId', BIN_TO_UUID(latest.Id, 0), 'latestReadyBytes', latest.SizeBytes, 'latestReadySha256', latest.ContentHash,
        'hasRowIdentityPrefix', IF(latest.Id IS NULL, NULL, IF(CAST(LEFT(latest.OriginalFileName, 15) AS BINARY) = 'reporting-rows-', TRUE, FALSE)))
    FROM fn_reporting_export_task AS t
    CROSS JOIN (
        SELECT COUNT(*) AS total, COUNT(CASE WHEN f.StatusKey = 'ready' THEN 1 END) AS ready,
            COUNT(CASE WHEN f.StatusKey = 'pending' THEN 1 END) AS pending
        FROM fn_files_tenant_resource_file AS f
        WHERE f.TenantId = @TenantId AND f.OwnerModuleKey = 'reporting' AND f.ResourceId = @TaskId
    ) AS counts
    LEFT JOIN fn_files_tenant_resource_file AS latest ON latest.Id = (
        SELECT f.Id FROM fn_files_tenant_resource_file AS f
        WHERE f.TenantId = @TenantId AND f.OwnerModuleKey = 'reporting' AND f.ResourceId = @TaskId AND f.StatusKey = 'ready'
        ORDER BY f.CreatedAtUtc DESC, f.Id DESC LIMIT 1
    )
    WHERE t.TenantId = @TenantId AND t.Id = @TaskId
), 'null');
