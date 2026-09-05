-- 122：为“我发起的”实例分页查询补充发起人索引。
SET @index_exists := (
    SELECT COUNT(DISTINCT INDEX_NAME)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_workflow_instance'
      AND INDEX_NAME = 'IX_fn_workflow_instance_Scope_StartedBy');
SET @ddl := IF(
    @index_exists = 0,
    'CREATE INDEX IX_fn_workflow_instance_Scope_StartedBy ON fn_workflow_instance (TenantScopeKey, StartedById, StartedAtUtc DESC, Id ASC)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
