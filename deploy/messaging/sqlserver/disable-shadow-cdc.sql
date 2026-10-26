-- 仅在停止采集窗口并确认维护范围后执行；不会删除 Legacy 或正式追加源。
-- 特权运维脚本，不能加入应用启动或 DbUp。
SET NOCOUNT ON;
IF EXISTS (SELECT 1 FROM cdc.change_tables WHERE capture_instance = N'fullnet_fn_messaging_shadow_event')
    EXEC sys.sp_cdc_disable_table @source_schema=N'dbo', @source_name=N'fn_messaging_shadow_event',
        @capture_instance=N'fullnet_fn_messaging_shadow_event';
SELECT capture_instance FROM cdc.change_tables WHERE capture_instance = N'fullnet_fn_messaging_shadow_event';
