-- Enable CDC for fn_messaging_outbox_event.
-- Privileged ops script: must NOT be run by DbUp, API, or Worker bootstrap.
-- Stable capture instance: fullnet_fn_messaging_outbox_event

SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = DB_NAME() AND is_cdc_enabled = 1)
BEGIN
    RAISERROR(N'Database-level CDC is not enabled. Enable CDC on the database before running this script.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'dbo.fn_messaging_outbox_event', N'U') IS NULL
BEGIN
    RAISERROR(N'Table dbo.fn_messaging_outbox_event does not exist. Apply migration 091 first.', 16, 1);
    RETURN;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM cdc.change_tables
    WHERE capture_instance = N'fullnet_fn_messaging_outbox_event'
)
BEGIN
    EXEC sys.sp_cdc_enable_table
        @source_schema = N'dbo',
        @source_name = N'fn_messaging_outbox_event',
        @role_name = NULL,
        @capture_instance = N'fullnet_fn_messaging_outbox_event',
        @supports_net_changes = 0;
END;

SELECT
    capture_instance,
    source_schema,
    source_object,
    start_lsn
FROM cdc.change_tables
WHERE capture_instance = N'fullnet_fn_messaging_outbox_event';