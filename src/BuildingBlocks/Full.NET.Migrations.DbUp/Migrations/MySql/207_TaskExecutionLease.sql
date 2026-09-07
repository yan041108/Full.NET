-- 207：导入/报表任务执行租约。崩溃后必须能按到期租约重新领取，禁止把进行中的任务永久卡死。
-- MySQL 缺少 ADD COLUMN IF NOT EXISTS，使用 INFORMATION_SCHEMA + PREPARE 保持可重入。升级时停止旧 API/Worker。

SET @column_ddl := IF(
    NOT EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_import_export_task'
          AND COLUMN_NAME = 'LeaseId'),
    'ALTER TABLE fn_import_export_task ADD COLUMN LeaseId binary(16) NULL COMMENT ''当前执行租约标识''',
    'SELECT 1');
PREPARE column_stmt FROM @column_ddl;
EXECUTE column_stmt;
DEALLOCATE PREPARE column_stmt;

SET @column_ddl := IF(
    NOT EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_import_export_task'
          AND COLUMN_NAME = 'LeaseExpiresAtUtc'),
    'ALTER TABLE fn_import_export_task ADD COLUMN LeaseExpiresAtUtc datetime(6) NULL COMMENT ''执行租约到期时间 UTC''',
    'SELECT 1');
PREPARE column_stmt FROM @column_ddl;
EXECUTE column_stmt;
DEALLOCATE PREPARE column_stmt;

SET @index_ddl := IF(
    NOT EXISTS (
        SELECT 1 FROM information_schema.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_import_export_task'
          AND INDEX_NAME = 'IX_fn_import_export_task_TenantId_StatusKey_LeaseExpiresAtUtc'),
    'CREATE INDEX IX_fn_import_export_task_TenantId_StatusKey_LeaseExpiresAtUtc ON fn_import_export_task (TenantId, StatusKey, LeaseExpiresAtUtc, CreatedAtUtc, Id)',
    'SELECT 1');
PREPARE index_stmt FROM @index_ddl;
EXECUTE index_stmt;
DEALLOCATE PREPARE index_stmt;

SET @column_ddl := IF(
    NOT EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_reporting_export_task'
          AND COLUMN_NAME = 'LeaseId'),
    'ALTER TABLE fn_reporting_export_task ADD COLUMN LeaseId binary(16) NULL COMMENT ''当前执行租约标识''',
    'SELECT 1');
PREPARE column_stmt FROM @column_ddl;
EXECUTE column_stmt;
DEALLOCATE PREPARE column_stmt;

SET @column_ddl := IF(
    NOT EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_reporting_export_task'
          AND COLUMN_NAME = 'LeaseExpiresAtUtc'),
    'ALTER TABLE fn_reporting_export_task ADD COLUMN LeaseExpiresAtUtc datetime(6) NULL COMMENT ''执行租约到期时间 UTC''',
    'SELECT 1');
PREPARE column_stmt FROM @column_ddl;
EXECUTE column_stmt;
DEALLOCATE PREPARE column_stmt;

SET @column_ddl := IF(
    NOT EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_reporting_export_task'
          AND COLUMN_NAME = 'ActorPermissionCodesJson'),
    'ALTER TABLE fn_reporting_export_task ADD COLUMN ActorPermissionCodesJson longtext NULL COMMENT ''创建时主体权限码快照 JSON，供 Worker 崩溃恢复重建授权''',
    'SELECT 1');
PREPARE column_stmt FROM @column_ddl;
EXECUTE column_stmt;
DEALLOCATE PREPARE column_stmt;

SET @index_ddl := IF(
    NOT EXISTS (
        SELECT 1 FROM information_schema.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_reporting_export_task'
          AND INDEX_NAME = 'IX_fn_reporting_export_task_TenantId_StatusKey_LeaseExpiresAtUtc'),
    'CREATE INDEX IX_fn_reporting_export_task_TenantId_StatusKey_LeaseExpiresAtUtc ON fn_reporting_export_task (TenantId, StatusKey, LeaseExpiresAtUtc, CreatedAtUtc, Id)',
    'SELECT 1');
PREPARE index_stmt FROM @index_ddl;
EXECUTE index_stmt;
DEALLOCATE PREPARE index_stmt;

SET @constraint_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_reporting_export_task'
      AND CONSTRAINT_NAME = 'CK_fn_reporting_export_task_StatusKey'
      AND CONSTRAINT_TYPE = 'CHECK');
SET @ddl := IF(
    @constraint_exists > 0,
    'ALTER TABLE fn_reporting_export_task DROP CHECK CK_fn_reporting_export_task_StatusKey',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @constraint_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_reporting_export_task'
      AND CONSTRAINT_NAME = 'CK_fn_reporting_export_task_StatusKey'
      AND CONSTRAINT_TYPE = 'CHECK');
SET @ddl := IF(
    @constraint_exists = 0,
    'ALTER TABLE fn_reporting_export_task ADD CONSTRAINT CK_fn_reporting_export_task_StatusKey CHECK (StatusKey IN (''queued'', ''processing'', ''succeeded'', ''failed''))',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
