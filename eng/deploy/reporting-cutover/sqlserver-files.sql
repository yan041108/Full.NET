-- 按租户与任务限制候选范围。保留孤立对象，但不输出文件名、对象键或上传人。
SET NOCOUNT ON;
DECLARE @TenantId uniqueidentifier = '{{TENANT_ID}}', @TaskId uniqueidentifier = '{{TASK_ID}}',
    @AfterFileId uniqueidentifier = CONVERT(uniqueidentifier, NULLIF('{{AFTER_FILE_ID}}', ''));
WITH primary_state AS (
    SELECT CASE WHEN DATABASEPROPERTYEX(DB_NAME(), 'Updateability') = 'READ_WRITE' THEN 1 ELSE 0 END AS IsWritable
)
SELECT TOP ({{TAKE}}) CASE WHEN p.IsWritable = 0 THEN '-1'
    ELSE CONCAT(CONVERT(char(36), f.Id), CHAR(9), f.StatusKey, CHAR(9), f.SizeBytes, CHAR(9), f.ContentHash, CHAR(9),
        CONVERT(varchar(26), CAST(SWITCHOFFSET(f.CreatedAtUtc, '+00:00') AS datetime2(6)), 126), 'Z', CHAR(9),
        CASE WHEN t.OutputFileId = f.Id THEN 1 ELSE 0 END) END
FROM primary_state AS p
LEFT JOIN dbo.fn_files_tenant_resource_file AS f
    ON p.IsWritable = 1 AND f.TenantId = @TenantId AND f.ResourceId = @TaskId
    AND f.OwnerModuleKey = 'reporting' AND f.StatusKey IN ('pending', 'ready')
    AND (@AfterFileId IS NULL OR f.Id > @AfterFileId)
LEFT JOIN dbo.fn_reporting_export_task AS t ON t.TenantId = f.TenantId AND t.Id = f.ResourceId
WHERE p.IsWritable = 0 OR f.Id IS NOT NULL
ORDER BY f.Id;
