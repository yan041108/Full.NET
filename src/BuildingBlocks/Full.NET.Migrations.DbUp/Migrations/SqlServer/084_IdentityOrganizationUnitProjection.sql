-- 084：Identity 消费 Organization 机构单元的本地投影表。

IF OBJECT_ID(N'dbo.fn_identity_organization_unit_projection', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_organization_unit_projection
    (
        TenantId uniqueidentifier NOT NULL,
        UnitId uniqueidentifier NOT NULL,
        Name nvarchar(128) NOT NULL,
        IsActive bit NOT NULL,
        SourceVersion bigint NOT NULL,
        SourceUpdatedAtUtc datetimeoffset(7) NOT NULL,
        ProjectedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_identity_organization_unit_projection
            PRIMARY KEY CLUSTERED (TenantId, UnitId)
    );
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份认证机构单元投影表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_organization_unit_projection';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_organization_unit_projection', @level2type=N'COLUMN', @level2name=N'IsActive';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_organization_unit_projection', @level2type=N'COLUMN', @level2name=N'Name';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'投影刷新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_organization_unit_projection', @level2type=N'COLUMN', @level2name=N'ProjectedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'源更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_organization_unit_projection', @level2type=N'COLUMN', @level2name=N'SourceUpdatedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'源版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_organization_unit_projection', @level2type=N'COLUMN', @level2name=N'SourceVersion';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_organization_unit_projection', @level2type=N'COLUMN', @level2name=N'TenantId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'机构单元标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_organization_unit_projection', @level2type=N'COLUMN', @level2name=N'UnitId';
END;
