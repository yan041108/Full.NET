-- 126：为工作流表单定义补充启停/归档状态与乐观并发版本列。

SET @column_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_workflow_form_definition'
      AND COLUMN_NAME = 'StatusKey');
SET @ddl := IF(
    @column_exists = 0,
    'ALTER TABLE fn_workflow_form_definition ADD COLUMN StatusKey VARCHAR(16) NOT NULL DEFAULT ''active''',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @column_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_workflow_form_definition'
      AND COLUMN_NAME = 'Version');
SET @ddl := IF(
    @column_exists = 0,
    'ALTER TABLE fn_workflow_form_definition ADD COLUMN Version BIGINT NOT NULL DEFAULT 1',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @constraint_exists := (
    SELECT COUNT(*)
    FROM information_schema.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_workflow_form_definition'
      AND CONSTRAINT_NAME = 'CK_fn_workflow_form_definition_StatusKey');
SET @ddl := IF(
    @constraint_exists = 0,
    'ALTER TABLE fn_workflow_form_definition ADD CONSTRAINT CK_fn_workflow_form_definition_StatusKey CHECK (StatusKey IN (''active'', ''disabled'', ''archived''))',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @constraint_exists := (
    SELECT COUNT(*)
    FROM information_schema.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_workflow_form_definition'
      AND CONSTRAINT_NAME = 'CK_fn_workflow_form_definition_Version');
SET @ddl := IF(
    @constraint_exists = 0,
    'ALTER TABLE fn_workflow_form_definition ADD CONSTRAINT CK_fn_workflow_form_definition_Version CHECK (Version > 0)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
