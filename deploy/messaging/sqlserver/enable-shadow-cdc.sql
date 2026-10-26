-- 独立影子源的特权运维脚本；禁止由 DbUp、API 或 Worker 自动执行。
-- 数据库级 CDC 与 SQL Server Agent 必须预先由运维启用。
SET NOCOUNT ON;
IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = DB_NAME() AND is_cdc_enabled = 1)
BEGIN
    RAISERROR(N'请先由运维启用数据库级 CDC。', 16, 1);
    RETURN;
END;
IF OBJECT_ID(N'dbo.fn_messaging_shadow_event', N'U') IS NULL
BEGIN
    RAISERROR(N'请先应用 296 双库迁移，再启用独立影子捕获。', 16, 1);
    RETURN;
END;
IF NOT EXISTS (SELECT 1 FROM cdc.change_tables WHERE capture_instance = N'fullnet_fn_messaging_shadow_event')
    EXEC sys.sp_cdc_enable_table @source_schema=N'dbo', @source_name=N'fn_messaging_shadow_event',
        @role_name=N'fullnet_shadow_reader', @capture_instance=N'fullnet_fn_messaging_shadow_event', @supports_net_changes=0;
SELECT capture.capture_instance, schemas.name AS SourceSchema, tables.name AS SourceTable, capture.start_lsn
FROM cdc.change_tables capture
JOIN sys.tables tables ON tables.object_id = capture.source_object_id
JOIN sys.schemas schemas ON schemas.schema_id = tables.schema_id
WHERE capture.capture_instance = N'fullnet_fn_messaging_shadow_event';
