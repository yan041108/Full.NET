-- 208：Host 空租户幂等唯一索引。MySQL 把 NULL TenantId 视为互异，必须用全零哨兵生成列闭合并发窗口。
DROP PROCEDURE IF EXISTS fn_mqtt_host_idempotency_scope_column;
DELIMITER $$
CREATE PROCEDURE fn_mqtt_host_idempotency_scope_column()
BEGIN
    IF NOT EXISTS
    (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_mqtt_message'
          AND COLUMN_NAME = 'ScopeTenantKey'
    ) THEN
        ALTER TABLE fn_mqtt_message ADD COLUMN ScopeTenantKey BINARY(16)
            GENERATED ALWAYS AS (COALESCE(TenantId, 0x00000000000000000000000000000000))
            STORED COMMENT '作用域租户键；Host 空租户使用全零哨兵以进入唯一索引';
    END IF;
END$$
DELIMITER ;
CALL fn_mqtt_host_idempotency_scope_column();
DROP PROCEDURE IF EXISTS fn_mqtt_host_idempotency_scope_column;

SET @index_ddl := IF(
    EXISTS (
        SELECT 1 FROM information_schema.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_mqtt_message'
          AND INDEX_NAME = 'UX_fn_mqtt_message_TenantId_IdempotencyKey'),
    'DROP INDEX UX_fn_mqtt_message_TenantId_IdempotencyKey ON fn_mqtt_message',
    'SELECT 1');
PREPARE index_stmt FROM @index_ddl;
EXECUTE index_stmt;
DEALLOCATE PREPARE index_stmt;

SET @index_ddl := IF(
    NOT EXISTS (
        SELECT 1 FROM information_schema.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_mqtt_message'
          AND INDEX_NAME = 'UX_fn_mqtt_message_ScopeTenantKey_IdempotencyKey'),
    'CREATE UNIQUE INDEX UX_fn_mqtt_message_ScopeTenantKey_IdempotencyKey ON fn_mqtt_message (ScopeTenantKey, IdempotencyKey)',
    'SELECT 1');
PREPARE index_stmt FROM @index_ddl;
EXECUTE index_stmt;
DEALLOCATE PREPARE index_stmt;
