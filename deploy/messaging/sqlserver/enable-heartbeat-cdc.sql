-- 心跳捕获属于特权运维配置，不由 API、Worker 或迁移自动启用。
SET NOCOUNT ON;
IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name=DB_NAME() AND is_cdc_enabled=1)
    THROW 51000, N'请先启用数据库级 CDC。', 1;
IF OBJECT_ID(N'dbo.fn_messaging_cdc_heartbeat', N'U') IS NULL
    THROW 51000, N'请先应用 352 双库迁移。', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.fn_messaging_cdc_heartbeat WHERE Id='01941f29-7c00-7000-8000-000000000001')
    THROW 51000, N'CDC 心跳单行缺失，请修复迁移基线。', 1;
IF EXISTS (SELECT 1 FROM cdc.change_tables WHERE capture_instance=N'fullnet_fn_messaging_cdc_heartbeat'
    AND source_object_id<>OBJECT_ID(N'dbo.fn_messaging_cdc_heartbeat'))
    THROW 51000, N'CDC 心跳捕获实例已绑定其他表。', 1;
IF NOT EXISTS (SELECT 1 FROM cdc.change_tables WHERE capture_instance=N'fullnet_fn_messaging_cdc_heartbeat')
    EXEC sys.sp_cdc_enable_table @source_schema=N'dbo', @source_name=N'fn_messaging_cdc_heartbeat',
        @role_name=N'fullnet_heartbeat_reader', @capture_instance=N'fullnet_fn_messaging_cdc_heartbeat', @supports_net_changes=0;
-- 首次启用时主动建立可捕获事务，避免 Connector 初始位点与首个自动心跳互相等待。
DECLARE @before binary(10)=sys.fn_cdc_get_max_lsn(), @seen int=0, @attempt int=0;
UPDATE dbo.fn_messaging_cdc_heartbeat SET UpdatedAtUtc=SYSUTCDATETIME()
WHERE Id='01941f29-7c00-7000-8000-000000000001';
WHILE @seen=0 AND @attempt<60
BEGIN
    EXEC sys.sp_executesql N'SELECT @result=COUNT(*) FROM cdc.fullnet_fn_messaging_cdc_heartbeat_CT WHERE Id=''01941f29-7c00-7000-8000-000000000001'' AND __$operation=4 AND (@previous IS NULL OR __$start_lsn>@previous)',
        N'@previous binary(10), @result int OUTPUT', @previous=@before, @result=@seen OUTPUT;
    IF @seen=0 WAITFOR DELAY '00:00:01';
    SET @attempt=@attempt+1;
END;
IF @seen=0 THROW 51000, N'CDC 心跳初始化未被捕获，请检查 SQL Server Agent 和捕获任务。', 1;
SELECT capture_instance, start_lsn FROM cdc.change_tables WHERE capture_instance=N'fullnet_fn_messaging_cdc_heartbeat';
