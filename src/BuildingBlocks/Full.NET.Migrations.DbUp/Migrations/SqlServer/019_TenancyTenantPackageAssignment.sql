-- 019：租户可选绑定 Host 套餐目录；解除绑定通过 TenantPackageId = NULL。
IF COL_LENGTH(N'dbo.fn_tenancy_tenant', N'TenantPackageId') IS NULL
    ALTER TABLE dbo.fn_tenancy_tenant
        ADD TenantPackageId uniqueidentifier NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant'), N'TenantPackageId', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户套餐标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant', @level2type=N'COLUMN', @level2name=N'TenantPackageId';

IF OBJECT_ID(N'dbo.fn_tenancy_tenant_package', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.fn_tenancy_tenant', N'TenantPackageId') IS NOT NULL
   AND NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_fn_tenancy_tenant_Package'
)
    ALTER TABLE dbo.fn_tenancy_tenant
        ADD CONSTRAINT FK_fn_tenancy_tenant_Package
            FOREIGN KEY (TenantPackageId) REFERENCES dbo.fn_tenancy_tenant_package(Id);
