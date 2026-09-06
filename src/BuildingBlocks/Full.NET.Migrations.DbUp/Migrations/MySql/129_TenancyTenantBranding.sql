ALTER TABLE fn_tenancy_tenant
    ADD COLUMN IF NOT EXISTS LogoFileId BINARY(16) NULL,
    ADD COLUMN IF NOT EXISTS SystemTitle varchar(128) NULL,
    ADD COLUMN IF NOT EXISTS ContactPhone varchar(32) NULL,
    ADD COLUMN IF NOT EXISTS ContactEmail varchar(256) NULL,
    ADD COLUMN IF NOT EXISTS ContactAddress varchar(512) NULL,
    ADD COLUMN IF NOT EXISTS Copyright varchar(256) NULL;

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
