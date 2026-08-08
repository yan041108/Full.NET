-- Disable CDC capture instance for fn_messaging_outbox_event.
-- Privileged ops script: run only during approved maintenance.

SET NOCOUNT ON;

IF EXISTS
(
    SELECT 1
    FROM cdc.change_tables
    WHERE capture_instance = N'fullnet_fn_messaging_outbox_event'
)
BEGIN
    EXEC sys.sp_cdc_disable_table
        @source_schema = N'dbo',
        @source_name = N'fn_messaging_outbox_event',
        @capture_instance = N'fullnet_fn_messaging_outbox_event';
END;

SELECT capture_instance
FROM cdc.change_tables
WHERE capture_instance = N'fullnet_fn_messaging_outbox_event';