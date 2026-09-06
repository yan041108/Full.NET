-- 173：ImportExport 导入任务批量执行状态与检查点列。

SET @column_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_import_export_task'
      AND COLUMN_NAME = 'ProcessedRowCount');
SET @ddl := IF(
    @column_exists = 0,
    'ALTER TABLE fn_import_export_task ADD COLUMN ProcessedRowCount int NOT NULL DEFAULT 0',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @column_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_import_export_task'
      AND COLUMN_NAME = 'SucceededRowCount');
SET @ddl := IF(
    @column_exists = 0,
    'ALTER TABLE fn_import_export_task ADD COLUMN SucceededRowCount int NOT NULL DEFAULT 0',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @column_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_import_export_task'
      AND COLUMN_NAME = 'ExecutionFailedRowCount');
SET @ddl := IF(
    @column_exists = 0,
    'ALTER TABLE fn_import_export_task ADD COLUMN ExecutionFailedRowCount int NOT NULL DEFAULT 0',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @column_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_import_export_task'
      AND COLUMN_NAME = 'NextLineNumber');
SET @ddl := IF(
    @column_exists = 0,
    'ALTER TABLE fn_import_export_task ADD COLUMN NextLineNumber int NOT NULL DEFAULT 0',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @column_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_import_export_task'
      AND COLUMN_NAME = 'ExecutionRowsJson');
SET @ddl := IF(
    @column_exists = 0,
    'ALTER TABLE fn_import_export_task ADD COLUMN ExecutionRowsJson longtext NULL',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @column_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_import_export_task'
      AND COLUMN_NAME = 'ErrorReceiptFileId');
SET @ddl := IF(
    @column_exists = 0,
    'ALTER TABLE fn_import_export_task ADD COLUMN ErrorReceiptFileId char(36) CHARACTER SET ascii COLLATE ascii_general_ci NULL',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @column_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_import_export_task'
      AND COLUMN_NAME = 'ExecutionStartedAtUtc');
SET @ddl := IF(
    @column_exists = 0,
    'ALTER TABLE fn_import_export_task ADD COLUMN ExecutionStartedAtUtc datetime(6) NULL',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @column_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_import_export_task'
      AND COLUMN_NAME = 'ExecutionCompletedAtUtc');
SET @ddl := IF(
    @column_exists = 0,
    'ALTER TABLE fn_import_export_task ADD COLUMN ExecutionCompletedAtUtc datetime(6) NULL',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @constraint_exists := (
    SELECT COUNT(*)
    FROM information_schema.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_import_export_task'
      AND CONSTRAINT_NAME = 'CK_fn_import_export_task_StatusKey');
SET @ddl := IF(
    @constraint_exists > 0,
    'ALTER TABLE fn_import_export_task DROP CHECK CK_fn_import_export_task_StatusKey',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @constraint_exists := (
    SELECT COUNT(*)
    FROM information_schema.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_import_export_task'
      AND CONSTRAINT_NAME = 'CK_fn_import_export_task_StatusKey');
SET @ddl := IF(
    @constraint_exists = 0,
    'ALTER TABLE fn_import_export_task ADD CONSTRAINT CK_fn_import_export_task_StatusKey CHECK (StatusKey IN (''uploaded'', ''preview_succeeded'', ''preview_failed'', ''queued'', ''executing'', ''execution_succeeded'', ''execution_partial'', ''execution_failed''))',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
