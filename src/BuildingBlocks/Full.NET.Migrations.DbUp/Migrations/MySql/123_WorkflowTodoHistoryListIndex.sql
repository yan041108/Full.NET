-- 123：为已办历史分页查询补充完成时间索引。
SET @index_exists := (
    SELECT COUNT(DISTINCT INDEX_NAME)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_workflow_todo'
      AND INDEX_NAME = 'IX_fn_workflow_todo_Assignee_Completed');
SET @ddl := IF(
    @index_exists = 0,
    'CREATE INDEX IX_fn_workflow_todo_Assignee_Completed ON fn_workflow_todo (AssigneeUserId, StatusKey, CompletedAtUtc DESC, Id ASC)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
