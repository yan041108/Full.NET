-- 直属汇报关系独立于部门负责人；所有写入在当前租户自有门闩内完成。
IF OBJECT_ID(N'dbo.fn_organization_reporting_guard',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_organization_reporting_guard (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        CONSTRAINT pk_fn_organization_reporting_guard PRIMARY KEY CLUSTERED (Id)
    );
END;
GO
IF OBJECT_ID(N'dbo.fn_organization_reporting_line',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_organization_reporting_line (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        EmployeeUserId uniqueidentifier NOT NULL,
        ManagerUserId uniqueidentifier NULL,
        Revision bigint NOT NULL,
        UpdatedById uniqueidentifier NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT pk_fn_organization_reporting_line PRIMARY KEY CLUSTERED (Id)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_organization_reporting_guard') AND name=N'ux_fn_organization_reporting_guard_tenant')
    ALTER TABLE dbo.fn_organization_reporting_guard ADD CONSTRAINT ux_fn_organization_reporting_guard_tenant UNIQUE (TenantId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_organization_reporting_line') AND name=N'ux_fn_organization_reporting_line_employee')
    ALTER TABLE dbo.fn_organization_reporting_line ADD CONSTRAINT ux_fn_organization_reporting_line_employee UNIQUE (TenantId,EmployeeUserId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_organization_reporting_line') AND name=N'fk_fn_organization_reporting_line_guard')
    ALTER TABLE dbo.fn_organization_reporting_line ADD CONSTRAINT fk_fn_organization_reporting_line_guard FOREIGN KEY (TenantId) REFERENCES fn_organization_reporting_guard(TenantId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_organization_reporting_line') AND name=N'ck_fn_organization_reporting_line_state')
    ALTER TABLE dbo.fn_organization_reporting_line ADD CONSTRAINT ck_fn_organization_reporting_line_state CHECK (Revision>0 AND EmployeeUserId<>'00000000-0000-0000-0000-000000000000' AND (ManagerUserId IS NULL OR (ManagerUserId<>EmployeeUserId AND ManagerUserId<>'00000000-0000-0000-0000-000000000000')));
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_organization_reporting_guard') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户直属汇报图写入串行门闩；不保存身份资料', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_reporting_guard';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_organization_reporting_guard') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_reporting_guard'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'应用端UUIDv7串行门闩标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_reporting_guard', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_organization_reporting_guard') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_reporting_guard'),N'TenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'可信租户标识；每个租户只有一个写门闩', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_reporting_guard', @level2type=N'COLUMN', @level2name=N'TenantId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_organization_reporting_line') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人员明确配置的直属汇报关系；不从部门或职级推定', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_reporting_line';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_organization_reporting_line') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_reporting_line'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'应用端UUIDv7汇报关系标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_reporting_line', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_organization_reporting_line') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_reporting_line'),N'TenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'认证上下文中的可信租户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_reporting_line', @level2type=N'COLUMN', @level2name=N'TenantId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_organization_reporting_line') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_reporting_line'),N'EmployeeUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前租户的人员标识；身份由Identity验证', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_reporting_line', @level2type=N'COLUMN', @level2name=N'EmployeeUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_organization_reporting_line') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_reporting_line'),N'ManagerUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'明确直属上级标识；空值表示没有直属上级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_reporting_line', @level2type=N'COLUMN', @level2name=N'ManagerUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_organization_reporting_line') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_reporting_line'),N'Revision','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'直属关系的乐观锁修订', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_reporting_line', @level2type=N'COLUMN', @level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_organization_reporting_line') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_reporting_line'),N'UpdatedById','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'认证上下文中的实际操作人', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_reporting_line', @level2type=N'COLUMN', @level2name=N'UpdatedById';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_organization_reporting_line') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_reporting_line'),N'UpdatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近变更时间；统一UTC微秒精度', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_reporting_line', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
GO
