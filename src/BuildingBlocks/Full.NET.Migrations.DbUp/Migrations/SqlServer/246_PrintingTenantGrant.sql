-- 246：Host 按不可变发布版本显式授权租户；升级不自动开放存量打印模板。
IF OBJECT_ID(N'dbo.fn_printing_template_tenant_grant', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_printing_template_tenant_grant (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        TemplateId uniqueidentifier NOT NULL,
        VersionNumber int NOT NULL,
        GrantedByUserId uniqueidentifier NOT NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_printing_template_tenant_grant PRIMARY KEY NONCLUSTERED (Id)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_printing_template_tenant_grant') AND name = N'UX_fn_printing_grant_Tenant_Template_Version')
    CREATE UNIQUE CLUSTERED INDEX UX_fn_printing_grant_Tenant_Template_Version ON dbo.fn_printing_template_tenant_grant (TenantId, TemplateId, VersionNumber);
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_printing_template_tenant_grant') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'打印模板发布版本租户授权', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template_tenant_grant';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_printing_template_tenant_grant') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_printing_template_tenant_grant'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template_tenant_grant', @level2type=N'COLUMN', @level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_printing_template_tenant_grant') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_printing_template_tenant_grant'), N'TenantId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'获授租户标识，不建立跨模块外键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template_tenant_grant', @level2type=N'COLUMN', @level2name=N'TenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_printing_template_tenant_grant') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_printing_template_tenant_grant'), N'TemplateId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本模块打印模板定义标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template_tenant_grant', @level2type=N'COLUMN', @level2name=N'TemplateId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_printing_template_tenant_grant') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_printing_template_tenant_grant'), N'VersionNumber', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'明确获授的不可变发布版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template_tenant_grant', @level2type=N'COLUMN', @level2name=N'VersionNumber';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_printing_template_tenant_grant') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_printing_template_tenant_grant'), N'GrantedByUserId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'授权操作人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template_tenant_grant', @level2type=N'COLUMN', @level2name=N'GrantedByUserId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_printing_template_tenant_grant') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_printing_template_tenant_grant'), N'CreatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'授权时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_printing_template_tenant_grant', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
