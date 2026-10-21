-- 273：索引部署可重试；静态 DDL 不经过动态 SQL。
DROP PROCEDURE IF EXISTS fn_settings_domain_audit_trace_index;
DELIMITER $$
CREATE PROCEDURE fn_settings_domain_audit_trace_index()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS
        WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_settings_domain_audit'
          AND INDEX_NAME='ix_fn_settings_domain_audit_trace_time') THEN
        CREATE INDEX ix_fn_settings_domain_audit_trace_time ON fn_settings_domain_audit (TraceId, OccurredAtUtc DESC);
    END IF;
END$$
DELIMITER ;
CALL fn_settings_domain_audit_trace_index();
DROP PROCEDURE IF EXISTS fn_settings_domain_audit_trace_index;
