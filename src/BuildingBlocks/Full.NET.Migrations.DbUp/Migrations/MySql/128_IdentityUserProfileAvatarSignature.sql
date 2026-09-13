SET @avatar_file_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_identity_user_profile'
      AND COLUMN_NAME = 'AvatarFileId');
SET @ddl := IF(
    @avatar_file_exists = 0,
    'ALTER TABLE fn_identity_user_profile ADD COLUMN AvatarFileId BINARY(16) NULL',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @signature_file_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_identity_user_profile'
      AND COLUMN_NAME = 'SignatureFileId');
SET @ddl := IF(
    @signature_file_exists = 0,
    'ALTER TABLE fn_identity_user_profile ADD COLUMN SignatureFileId BINARY(16) NULL',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @index_exists := (
    SELECT COUNT(1)
    FROM information_schema.statistics
    WHERE table_schema = DATABASE()
      AND table_name = 'fn_identity_user_profile'
      AND index_name = 'IX_fn_identity_user_profile_AvatarFileId');
SET @ddl := IF(
    @index_exists = 0,
    'CREATE INDEX IX_fn_identity_user_profile_AvatarFileId ON fn_identity_user_profile (AvatarFileId)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @index_exists := (
    SELECT COUNT(1)
    FROM information_schema.statistics
    WHERE table_schema = DATABASE()
      AND table_name = 'fn_identity_user_profile'
      AND index_name = 'IX_fn_identity_user_profile_SignatureFileId');
SET @ddl := IF(
    @index_exists = 0,
    'CREATE INDEX IX_fn_identity_user_profile_SignatureFileId ON fn_identity_user_profile (SignatureFileId)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
