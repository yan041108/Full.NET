-- 019：租户可选绑定 Host 套餐目录；解除绑定通过 TenantPackageId = NULL。
SET @tenant_package_id_ddl = IF(
    EXISTS(
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_tenancy_tenant'
          AND COLUMN_NAME = 'TenantPackageId'),
    'SELECT 1',
    'ALTER TABLE fn_tenancy_tenant ADD COLUMN TenantPackageId BINARY(16) NULL');
PREPARE tenant_package_id_statement FROM @tenant_package_id_ddl;
EXECUTE tenant_package_id_statement;
DEALLOCATE PREPARE tenant_package_id_statement;

SET @tenant_package_fk_ddl = IF(
    EXISTS(
        SELECT 1
        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_tenancy_tenant'
          AND CONSTRAINT_NAME = 'FK_fn_tenancy_tenant_Package'),
    'SELECT 1',
    'ALTER TABLE fn_tenancy_tenant ADD CONSTRAINT FK_fn_tenancy_tenant_Package FOREIGN KEY (TenantPackageId) REFERENCES fn_tenancy_tenant_package(Id)');
PREPARE tenant_package_fk_statement FROM @tenant_package_fk_ddl;
EXECUTE tenant_package_fk_statement;
DEALLOCATE PREPARE tenant_package_fk_statement;
