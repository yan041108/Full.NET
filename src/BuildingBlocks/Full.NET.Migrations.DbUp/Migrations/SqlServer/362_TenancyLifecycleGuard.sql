-- 生命周期与运营禁用共用事务行锁；基础设施门闩不保存租户或用户资料。
IF OBJECT_ID(N'dbo.fn_tenancy_lifecycle_guard',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_tenancy_lifecycle_guard (
        Id uniqueidentifier NOT NULL,
        Version bigint NOT NULL,
        CONSTRAINT PK_fn_tenancy_lifecycle_guard PRIMARY KEY CLUSTERED (Id)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM dbo.fn_tenancy_lifecycle_guard WHERE Id='019a4906-51a0-7000-8000-000000000001')
    INSERT INTO dbo.fn_tenancy_lifecycle_guard(Id,Version) VALUES ('019a4906-51a0-7000-8000-000000000001',1);
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_lifecycle_guard') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'租户停止操作的全局事务串行门闩',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_lifecycle_guard';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_lifecycle_guard') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_lifecycle_guard'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'固定的基础设施 UUID v7 标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_lifecycle_guard',@level2type=N'COLUMN',@level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_tenancy_lifecycle_guard') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_lifecycle_guard'),N'Version','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'领取事务锁时递增的修订',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_lifecycle_guard',@level2type=N'COLUMN',@level2name=N'Version';
