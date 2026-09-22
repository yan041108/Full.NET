SET @column_exists := (
    SELECT COUNT(1)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_identity_user'
      AND COLUMN_NAME = 'RetiredAtUtc'
);

SET @ddl := IF(
    @column_exists = 0,
    'ALTER TABLE fn_identity_user ADD COLUMN RetiredAtUtc datetime(6) NULL COMMENT ''Host 用户退役时间（UTC）；非空表示不可再启用''',
    'SELECT 1'
);

PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
