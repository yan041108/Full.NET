-- 036：职位可选绑定当前租户职级；应用层负责校验租户一致性与职级启用状态。
IF COL_LENGTH(N'dbo.fn_organization_position', N'PositionLevelId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_organization_position
        ADD PositionLevelId uniqueidentifier NULL;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_organization_position')
      AND name = N'FK_fn_organization_position_PositionLevel'
)
BEGIN
    EXEC(N'
        ALTER TABLE dbo.fn_organization_position
            ADD CONSTRAINT FK_fn_organization_position_PositionLevel
                FOREIGN KEY (PositionLevelId)
                REFERENCES dbo.fn_organization_position_level(Id)');
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_organization_position')
      AND name = N'IX_fn_organization_position_Tenant_PositionLevel'
)
BEGIN
    EXEC(N'
        CREATE INDEX IX_fn_organization_position_Tenant_PositionLevel
            ON dbo.fn_organization_position(TenantId, PositionLevelId)
            WHERE PositionLevelId IS NOT NULL');
END;
