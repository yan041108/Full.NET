-- 组织模块持久接收注册分配意图；无跨模块外键，重复迁移不会改写既有意图。
IF OBJECT_ID(N'dbo.fn_organization_registration_assignment', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_organization_registration_assignment (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        MemberId uniqueidentifier NOT NULL,
        MemberVersion int NOT NULL,
        UserId uniqueidentifier NOT NULL,
        UnitId uniqueidentifier NOT NULL,
        PositionId uniqueidentifier NULL,
        Status varchar(16) NOT NULL,
        ErrorCode varchar(160) NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        DueAtUtc datetime2(6) NOT NULL,
        Version bigint NOT NULL,
        CONSTRAINT PK_fn_organization_registration_assignment PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_organization_registration_assignment_Status CHECK (Status IN ('Pending','Completed','Skipped')),
        CONSTRAINT CK_fn_organization_registration_assignment_Version CHECK (Version>0 AND MemberVersion>0)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_organization_registration_assignment') AND name=N'IX_fn_organization_registration_assignment_Due')
    CREATE INDEX IX_fn_organization_registration_assignment_Due ON dbo.fn_organization_registration_assignment (Status,DueAtUtc,Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_organization_registration_assignment') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'注册方式组织分配的持久恢复意图',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_organization_registration_assignment';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_organization_registration_assignment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_registration_assignment'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'注册分配意图的应用端 UUID v7',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_organization_registration_assignment',@level2type=N'COLUMN',@level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_organization_registration_assignment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_registration_assignment'),N'TenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可信租户标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_organization_registration_assignment',@level2type=N'COLUMN',@level2name=N'TenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_organization_registration_assignment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_registration_assignment'),N'MemberId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'来源成员关系标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_organization_registration_assignment',@level2type=N'COLUMN',@level2name=N'MemberId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_organization_registration_assignment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_registration_assignment'),N'MemberVersion','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'激活后的成员版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_organization_registration_assignment',@level2type=N'COLUMN',@level2name=N'MemberVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_organization_registration_assignment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_registration_assignment'),N'UserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'目标账号标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_organization_registration_assignment',@level2type=N'COLUMN',@level2name=N'UserId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_organization_registration_assignment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_registration_assignment'),N'UnitId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'目标机构单元标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_organization_registration_assignment',@level2type=N'COLUMN',@level2name=N'UnitId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_organization_registration_assignment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_registration_assignment'),N'PositionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'可选目标职位标识',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_organization_registration_assignment',@level2type=N'COLUMN',@level2name=N'PositionId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_organization_registration_assignment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_registration_assignment'),N'Status','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'Pending、Completed 或 Skipped',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_organization_registration_assignment',@level2type=N'COLUMN',@level2name=N'Status';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_organization_registration_assignment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_registration_assignment'),N'ErrorCode','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'最近一次稳定错误码',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_organization_registration_assignment',@level2type=N'COLUMN',@level2name=N'ErrorCode';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_organization_registration_assignment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_registration_assignment'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'激活事实时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_organization_registration_assignment',@level2type=N'COLUMN',@level2name=N'CreatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_organization_registration_assignment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_registration_assignment'),N'DueAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'下次处理时间',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_organization_registration_assignment',@level2type=N'COLUMN',@level2name=N'DueAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_organization_registration_assignment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_registration_assignment'),N'Version','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本地并发版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_organization_registration_assignment',@level2type=N'COLUMN',@level2name=N'Version';

