SET @logo_file_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_tenancy_tenant'
      AND COLUMN_NAME = 'LogoFileId');
SET @ddl := IF(
    @logo_file_exists = 0,
    'ALTER TABLE fn_tenancy_tenant ADD COLUMN LogoFileId BINARY(16) NULL',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @system_title_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_tenancy_tenant'
      AND COLUMN_NAME = 'SystemTitle');
SET @ddl := IF(
    @system_title_exists = 0,
    'ALTER TABLE fn_tenancy_tenant ADD COLUMN SystemTitle varchar(128) NULL',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @contact_phone_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_tenancy_tenant'
      AND COLUMN_NAME = 'ContactPhone');
SET @ddl := IF(
    @contact_phone_exists = 0,
    'ALTER TABLE fn_tenancy_tenant ADD COLUMN ContactPhone varchar(32) NULL',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @contact_email_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_tenancy_tenant'
      AND COLUMN_NAME = 'ContactEmail');
SET @ddl := IF(
    @contact_email_exists = 0,
    'ALTER TABLE fn_tenancy_tenant ADD COLUMN ContactEmail varchar(256) NULL',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @contact_address_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_tenancy_tenant'
      AND COLUMN_NAME = 'ContactAddress');
SET @ddl := IF(
    @contact_address_exists = 0,
    'ALTER TABLE fn_tenancy_tenant ADD COLUMN ContactAddress varchar(512) NULL',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @copyright_exists := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_tenancy_tenant'
      AND COLUMN_NAME = 'Copyright');
SET @ddl := IF(
    @copyright_exists = 0,
    'ALTER TABLE fn_tenancy_tenant ADD COLUMN Copyright varchar(256) NULL',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @index_exists := (
    SELECT COUNT(1)
    FROM information_schema.statistics
    WHERE table_schema = DATABASE()
      AND table_name = 'fn_tenancy_tenant'
      AND index_name = 'IX_fn_tenancy_tenant_LogoFileId');
SET @ddl := IF(
    @index_exists = 0,
    'CREATE INDEX IX_fn_tenancy_tenant_LogoFileId ON fn_tenancy_tenant (LogoFileId)',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
