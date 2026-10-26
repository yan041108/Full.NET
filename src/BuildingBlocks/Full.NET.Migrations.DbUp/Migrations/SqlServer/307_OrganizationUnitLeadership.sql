-- 负责人必须由明确授权配置；不把职级排序或任一隶属成员回填成负责人。
IF OBJECT_ID(N'dbo.fn_organization_unit_leadership',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_organization_unit_leadership (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        LeaderUserId uniqueidentifier NULL,
        Revision bigint NOT NULL,
        UpdatedById uniqueidentifier NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT pk_fn_organization_unit_leadership PRIMARY KEY CLUSTERED (Id)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_organization_unit_leadership') AND name=N'fk_fn_organization_unit_leadership_unit')
    ALTER TABLE dbo.fn_organization_unit_leadership ADD CONSTRAINT fk_fn_organization_unit_leadership_unit
        FOREIGN KEY (Id) REFERENCES dbo.fn_organization_unit(Id) ON DELETE CASCADE;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_organization_unit_leadership') AND name=N'ck_fn_organization_unit_leadership_state')
    ALTER TABLE dbo.fn_organization_unit_leadership WITH CHECK ADD CONSTRAINT ck_fn_organization_unit_leadership_state
        CHECK (Revision>0 AND (LeaderUserId IS NULL OR LeaderUserId<>'00000000-0000-0000-0000-000000000000'));
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_organization_unit_leadership') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'机构明确授权的负责人配置；不从隶属成员推定', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_unit_leadership';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_organization_unit_leadership') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_unit_leadership'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'复用机构逻辑主键的一对一配置标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_unit_leadership', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_organization_unit_leadership') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_unit_leadership'),N'TenantId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'由当前可信租户及所属机构确定的租户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_unit_leadership', @level2type=N'COLUMN', @level2name=N'TenantId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_organization_unit_leadership') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_unit_leadership'),N'LeaderUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'明确配置的负责人；空值表示尚无负责人', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_unit_leadership', @level2type=N'COLUMN', @level2name=N'LeaderUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_organization_unit_leadership') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_unit_leadership'),N'Revision','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'负责人配置的乐观锁修订', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_unit_leadership', @level2type=N'COLUMN', @level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_organization_unit_leadership') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_unit_leadership'),N'UpdatedById','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'认证上下文中的实际配置人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_unit_leadership', @level2type=N'COLUMN', @level2name=N'UpdatedById';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_organization_unit_leadership') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_unit_leadership'),N'UpdatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近配置时间；统一UTC微秒精度', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_unit_leadership', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
GO
