-- 124：为工作流定义/版本与实例补充业务标题模板与启动快照列。
SET @column_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_workflow_definition'
      AND COLUMN_NAME = 'BusinessTitleTemplate');
SET @ddl := IF(
    @column_exists = 0,
    'ALTER TABLE fn_workflow_definition ADD COLUMN BusinessTitleTemplate VARCHAR(256) NULL',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @column_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_workflow_definition_version'
      AND COLUMN_NAME = 'BusinessTitleTemplate');
SET @ddl := IF(
    @column_exists = 0,
    'ALTER TABLE fn_workflow_definition_version ADD COLUMN BusinessTitleTemplate VARCHAR(256) NULL',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @column_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_workflow_instance'
      AND COLUMN_NAME = 'BusinessTitle');
SET @ddl := IF(
    @column_exists = 0,
    'ALTER TABLE fn_workflow_instance ADD COLUMN BusinessTitle VARCHAR(256) NULL',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
