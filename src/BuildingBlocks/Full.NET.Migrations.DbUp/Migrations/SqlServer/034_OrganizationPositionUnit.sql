-- 034：职位可选绑定当前租户机构；应用层负责校验租户一致性与机构启用状态。
IF COL_LENGTH(N'dbo.fn_organization_position', N'UnitId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_organization_position
        ADD UnitId uniqueidentifier NULL;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_organization_position')
      AND name = N'FK_fn_organization_position_Unit'
)
BEGIN
    EXEC(N'
        ALTER TABLE dbo.fn_organization_position
            ADD CONSTRAINT FK_fn_organization_position_Unit
                FOREIGN KEY (UnitId) REFERENCES dbo.fn_organization_unit(Id)');
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_organization_position')
      AND name = N'IX_fn_organization_position_Tenant_Unit'
)
BEGIN
    EXEC(N'
        CREATE INDEX IX_fn_organization_position_Tenant_Unit
            ON dbo.fn_organization_position(TenantId, UnitId)
            WHERE UnitId IS NOT NULL');
END;
