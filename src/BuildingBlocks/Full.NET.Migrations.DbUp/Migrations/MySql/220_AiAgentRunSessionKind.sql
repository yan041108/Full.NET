-- 220：持久化 Agent 运行绑定的会话来源，供 Worker 恢复时选择 OIDC 或旧刷新会话校验路径。
SET @column_exists := (
    SELECT COUNT(1)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_ai_agent_run'
      AND COLUMN_NAME = 'SessionKind');
SET @ddl := IF(
    @column_exists = 0,
    'ALTER TABLE fn_ai_agent_run ADD COLUMN SessionKind varchar(32) NOT NULL DEFAULT ''refresh''',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
