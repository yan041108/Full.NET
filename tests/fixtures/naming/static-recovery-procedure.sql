DROP PROCEDURE IF EXISTS fn_settings_trace_recovery;
DELIMITER $$
CREATE PROCEDURE fn_settings_trace_recovery()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE()) THEN
        CREATE INDEX IX_fn_settings_config_entry_UpdatedAtUtc ON fn_settings_config_entry (UpdatedAtUtc);
    END IF;
END$$
DELIMITER ;
CALL fn_settings_trace_recovery();
DROP PROCEDURE IF EXISTS fn_settings_trace_recovery;
