-- 用户管理筛选的本地读投影，不参与授权。
IF OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection', N'U') IS NULL
BEGIN
CREATE TABLE dbo.fn_identity_organization_user_unit_projection (
    TenantId uniqueidentifier NOT NULL,
    AssignmentId uniqueidentifier NOT NULL,
    UserId uniqueidentifier NOT NULL,
    UnitId uniqueidentifier NOT NULL,
    IsActive bit NOT NULL,
    SourceVersion bigint NOT NULL,
    SourceUpdatedAtUtc datetimeoffset(7) NOT NULL,
    CONSTRAINT PK_fn_identity_organization_user_unit_projection PRIMARY KEY CLUSTERED (TenantId, AssignmentId)
);
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份模块机构隶属读投影', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_organization_user_unit_projection';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection'), N'AssignmentId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'源隶属标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_organization_user_unit_projection', @level2type=N'COLUMN', @level2name=N'AssignmentId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection'), N'IsActive', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_organization_user_unit_projection', @level2type=N'COLUMN', @level2name=N'IsActive';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection'), N'SourceUpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'源更新时间 UTC', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_organization_user_unit_projection', @level2type=N'COLUMN', @level2name=N'SourceUpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection'), N'SourceVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'源版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_organization_user_unit_projection', @level2type=N'COLUMN', @level2name=N'SourceVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_organization_user_unit_projection', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection'), N'UnitId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'部门标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_organization_user_unit_projection', @level2type=N'COLUMN', @level2name=N'UnitId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection'), N'UserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_organization_user_unit_projection', @level2type=N'COLUMN', @level2name=N'UserId';
END;
-- 表和注释可能已落地而索引步骤尚未完成；重放必须补建读取索引。
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection')
    AND name=N'IX_fn_identity_organization_user_unit_projection_UnitUser')
CREATE INDEX IX_fn_identity_organization_user_unit_projection_UnitUser ON dbo.fn_identity_organization_user_unit_projection(TenantId, UnitId, IsActive, UserId);
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection') AND minor_id=0 AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'身份模块机构隶属读投影', @level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_organization_user_unit_projection';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection'),N'TenantId','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识', @level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_organization_user_unit_projection',@level2type=N'COLUMN',@level2name=N'TenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection'),N'AssignmentId','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'源隶属标识', @level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_organization_user_unit_projection',@level2type=N'COLUMN',@level2name=N'AssignmentId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection'),N'UserId','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_organization_user_unit_projection',@level2type=N'COLUMN',@level2name=N'UserId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection'),N'UnitId','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'部门标识', @level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_organization_user_unit_projection',@level2type=N'COLUMN',@level2name=N'UnitId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection'),N'IsActive','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_organization_user_unit_projection',@level2type=N'COLUMN',@level2name=N'IsActive';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection'),N'SourceVersion','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'源版本', @level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_organization_user_unit_projection',@level2type=N'COLUMN',@level2name=N'SourceVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_organization_user_unit_projection'),N'SourceUpdatedAtUtc','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'源更新时间 UTC', @level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_identity_organization_user_unit_projection',@level2type=N'COLUMN',@level2name=N'SourceUpdatedAtUtc';
IF COL_LENGTH('dbo.fn_organization_user_unit','PublishedProjectionVersion') IS NULL
ALTER TABLE dbo.fn_organization_user_unit ADD PublishedProjectionVersion int NOT NULL CONSTRAINT DF_fn_organization_user_unit_PublishedProjectionVersion DEFAULT 0;

IF NOT EXISTS (

    SELECT 1

    FROM sys.extended_properties

    WHERE class = 1

      AND major_id = OBJECT_ID(N'dbo.fn_organization_user_unit')

      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_user_unit'), N'PublishedProjectionVersion', 'ColumnId')

      AND name = N'MS_Description'

)

    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已发布读投影源版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_organization_user_unit', @level2type=N'COLUMN', @level2name=N'PublishedProjectionVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_organization_user_unit') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_organization_user_unit'),N'PublishedProjectionVersion','ColumnId') AND name=N'MS_Description')
EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'已发布读投影源版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_organization_user_unit',@level2type=N'COLUMN',@level2name=N'PublishedProjectionVersion';
