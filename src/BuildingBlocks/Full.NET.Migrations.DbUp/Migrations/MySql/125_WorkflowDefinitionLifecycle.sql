-- 125：为工作流定义补充启停/归档状态列。

SET @column_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_workflow_definition'
      AND COLUMN_NAME = 'StatusKey');
SET @ddl := IF(
    @column_exists = 0,
    'ALTER TABLE fn_workflow_definition ADD COLUMN StatusKey VARCHAR(16) NOT NULL DEFAULT ''active''',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @constraint_exists := (
    SELECT COUNT(*)
    FROM information_schema.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_workflow_definition'
      AND CONSTRAINT_NAME = 'CK_fn_workflow_definition_StatusKey');
SET @ddl := IF(
    @constraint_exists = 0,
    'ALTER TABLE fn_workflow_definition ADD CONSTRAINT CK_fn_workflow_definition_StatusKey CHECK (StatusKey IN (''active'', ''disabled'', ''archived''))',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
