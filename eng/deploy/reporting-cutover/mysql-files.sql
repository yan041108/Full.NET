-- 候选按 Binary16 的原生顺序分页，日期按表中的 UTC 值输出。
SET SESSION lock_wait_timeout = 15;
SET @TenantId = UUID_TO_BIN('{{TENANT_ID}}', 0), @TaskId = UUID_TO_BIN('{{TASK_ID}}', 0),
    @AfterFileId = UUID_TO_BIN(NULLIF('{{AFTER_FILE_ID}}', ''), 0);
WITH primary_state AS (
    SELECT @@global.read_only = 0 AND @@global.super_read_only = 0 AS IsWritable
)
SELECT /*+ MAX_EXECUTION_TIME(15000) */ CASE WHEN p.IsWritable = 0 THEN '-1'
    ELSE CONCAT(BIN_TO_UUID(f.Id, 0), CHAR(9), f.StatusKey, CHAR(9), f.SizeBytes, CHAR(9), f.ContentHash, CHAR(9),
        DATE_FORMAT(f.CreatedAtUtc, '%Y-%m-%dT%H:%i:%s.%fZ'), CHAR(9), IF(t.OutputFileId = f.Id, 1, 0)) END
FROM primary_state AS p
LEFT JOIN fn_files_tenant_resource_file AS f
    ON p.IsWritable = 1 AND f.TenantId = @TenantId AND f.ResourceId = @TaskId
    AND f.OwnerModuleKey = 'reporting' AND f.StatusKey IN ('pending', 'ready')
    AND (@AfterFileId IS NULL OR f.Id > @AfterFileId)
LEFT JOIN fn_reporting_export_task AS t ON t.TenantId = f.TenantId AND t.Id = f.ResourceId
WHERE p.IsWritable = 0 OR f.Id IS NOT NULL
ORDER BY f.Id LIMIT {{TAKE}};
