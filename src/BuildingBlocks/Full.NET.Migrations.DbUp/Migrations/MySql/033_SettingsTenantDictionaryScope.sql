-- 033：数据字典类型扩展租户作用域（Host 行 TenantId 为 NULL）。

SET @hasTenantId := (
    SELECT COUNT(1)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_settings_dict_type'
      AND COLUMN_NAME = 'TenantId');

SET @ddl := IF(
    @hasTenantId = 0,
    'ALTER TABLE fn_settings_dict_type ADD COLUMN TenantId BINARY(16) NULL AFTER Id',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @hasOldUx := (
    SELECT COUNT(1)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_settings_dict_type'
      AND INDEX_NAME = 'UX_fn_settings_dict_type_Code');

SET @dropUx := IF(
    @hasOldUx > 0,
    'ALTER TABLE fn_settings_dict_type DROP INDEX UX_fn_settings_dict_type_Code',
    'SELECT 1');
PREPARE stmt FROM @dropUx;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @hasScopeUx := (
    SELECT COUNT(1)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_settings_dict_type'
      AND INDEX_NAME = 'UX_fn_settings_dict_type_Scope_Code');

SET @createUx := IF(
    @hasScopeUx = 0,
    'CREATE UNIQUE INDEX UX_fn_settings_dict_type_Scope_Code ON fn_settings_dict_type ((COALESCE(TenantId, 0x0000000000000000000000000000000000)), Code)',
    'SELECT 1');
PREPARE stmt FROM @createUx;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
