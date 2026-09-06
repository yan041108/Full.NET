-- 140：为 OpenAccess 接入方应用增加每日请求配额上限（NULL 表示不限）。

SET @column_exists = (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_identity_open_access_client'
      AND COLUMN_NAME = 'DailyRequestQuota');

SET @ddl = IF(
    @column_exists = 0,
    'ALTER TABLE fn_identity_open_access_client ADD COLUMN DailyRequestQuota int NULL',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
