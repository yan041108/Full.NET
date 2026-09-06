SET @must_change_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_identity_user'
      AND COLUMN_NAME = 'MustChangePassword');

SET @ddl := IF(
    @must_change_exists = 0,
    'ALTER TABLE fn_identity_user ADD COLUMN MustChangePassword TINYINT(1) NOT NULL DEFAULT 0',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @password_changed_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_identity_user'
      AND COLUMN_NAME = 'PasswordChangedAtUtc');

SET @ddl := IF(
    @password_changed_exists = 0,
    'ALTER TABLE fn_identity_user ADD COLUMN PasswordChangedAtUtc datetime(6) NULL',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

UPDATE fn_identity_user
SET PasswordChangedAtUtc = COALESCE(UpdatedAtUtc, CreatedAtUtc)
WHERE PasswordChangedAtUtc IS NULL;
