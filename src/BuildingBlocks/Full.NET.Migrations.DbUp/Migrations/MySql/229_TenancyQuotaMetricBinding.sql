-- 229：新预留绑定实际配额记录；历史归属没有可靠证据，保留 NULL 等待对账。
SET @quota_metric_id_exists := (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_tenancy_quota_reservation' AND COLUMN_NAME = 'MetricId'
);
SET @quota_metric_binding_ddl := IF(@quota_metric_id_exists = 0,
    'ALTER TABLE fn_tenancy_quota_reservation ADD COLUMN MetricId BINARY(16) NULL COMMENT ''实际配额记录标识；NULL 表示历史归属待对账''',
    'SELECT 1');
PREPARE quota_metric_binding_stmt FROM @quota_metric_binding_ddl;
EXECUTE quota_metric_binding_stmt;
DEALLOCATE PREPARE quota_metric_binding_stmt;
