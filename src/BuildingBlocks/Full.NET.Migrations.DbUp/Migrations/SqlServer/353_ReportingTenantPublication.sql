-- Host 定义显式发布到租户；无跨模块外键，旧导出不补造原会话。
IF OBJECT_ID(N'dbo.fn_reporting_definition_tenant_grant', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_reporting_definition_tenant_grant (
        Id uniqueidentifier NOT NULL,
        DefinitionId uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        IsEnabled bit NOT NULL,
        Version int NOT NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        CreatedByUserId uniqueidentifier NOT NULL,
        UpdatedAtUtc datetime2(6) NULL,
        UpdatedByUserId uniqueidentifier NULL,
        CONSTRAINT PK_fn_reporting_definition_tenant_grant PRIMARY KEY CLUSTERED (Id)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_reporting_definition_tenant_grant') AND name=N'UX_fn_reporting_def_grant_Tenant_Definition')
    CREATE UNIQUE INDEX UX_fn_reporting_def_grant_Tenant_Definition ON dbo.fn_reporting_definition_tenant_grant(TenantId,DefinitionId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_reporting_definition_tenant_grant') AND name=N'IX_fn_reporting_def_grant_Definition')
    CREATE INDEX IX_fn_reporting_def_grant_Definition ON dbo.fn_reporting_definition_tenant_grant(DefinitionId,TenantId);
IF COL_LENGTH(N'dbo.fn_reporting_export_task', N'ActorSessionBindingJson') IS NULL
    ALTER TABLE dbo.fn_reporting_export_task ADD ActorSessionBindingJson nvarchar(2048) NULL;
GO
-- 说明逐项幂等补齐，DDL 中断后重跑不会覆盖已有授权或业务数据。
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_reporting_definition_tenant_grant')
    AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition_tenant_grant'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建 UTC 时间',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_reporting_definition_tenant_grant',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_reporting_definition_tenant_grant')
    AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition_tenant_grant'),N'CreatedByUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信创建主体',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_reporting_definition_tenant_grant',@level2type=N'COLUMN',@level2name=N'CreatedByUserId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_reporting_definition_tenant_grant')
    AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition_tenant_grant'),N'DefinitionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本模块定义身份',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_reporting_definition_tenant_grant',@level2type=N'COLUMN',@level2name=N'DefinitionId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_reporting_definition_tenant_grant')
    AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition_tenant_grant'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'应用端 UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_reporting_definition_tenant_grant',@level2type=N'COLUMN',@level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_reporting_definition_tenant_grant')
    AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition_tenant_grant'),N'IsEnabled','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'显式发布授权启用状态',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_reporting_definition_tenant_grant',@level2type=N'COLUMN',@level2name=N'IsEnabled';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_reporting_definition_tenant_grant')
    AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition_tenant_grant'),N'TenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'Host 明确授权的目标租户，无跨模块外键',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_reporting_definition_tenant_grant',@level2type=N'COLUMN',@level2name=N'TenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_reporting_definition_tenant_grant')
    AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition_tenant_grant'),N'UpdatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'更新 UTC 时间',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_reporting_definition_tenant_grant',@level2type=N'COLUMN',@level2name=N'UpdatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_reporting_definition_tenant_grant')
    AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition_tenant_grant'),N'UpdatedByUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信更新主体',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_reporting_definition_tenant_grant',@level2type=N'COLUMN',@level2name=N'UpdatedByUserId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_reporting_definition_tenant_grant')
    AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_definition_tenant_grant'),N'Version','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'授权并发版本',@level0type=N'SCHEMA',@level0name=N'dbo',
        @level1type=N'TABLE',@level1name=N'fn_reporting_definition_tenant_grant',@level2type=N'COLUMN',@level2name=N'Version';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_reporting_definition_tenant_grant') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'报表定义到租户的显式发布授权',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_reporting_definition_tenant_grant';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_reporting_export_task') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_reporting_export_task'),N'ActorSessionBindingJson','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'私有原会话可信绑定；缺失时执行和下载失败关闭',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_reporting_export_task',@level2type=N'COLUMN',@level2name=N'ActorSessionBindingJson';
GO
