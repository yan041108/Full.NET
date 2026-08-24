-- 为 Identity 角色增加超级管理员标记并回填受保护系统角色；本迁移不删除数据。
-- SQL Server 通过元数据检查和动态 SQL 支持“DDL 已完成但 DbUp 尚未记账”的恢复路径，各步骤可独立收敛。
IF COL_LENGTH(N'dbo.fn_identity_role', N'IsSuperAdministrator') IS NULL
    ALTER TABLE dbo.fn_identity_role ADD IsSuperAdministrator bit NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_identity_role')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_role'), N'IsSuperAdministrator', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否超级管理员角色', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_role', @level2type=N'COLUMN', @level2name=N'IsSuperAdministrator';

EXEC(N'
UPDATE dbo.fn_identity_role
SET IsSuperAdministrator = CASE
    WHEN ScopeKey = ''host'' AND Code = ''host-administrator'' THEN 1
    ELSE 0
END
WHERE IsSuperAdministrator IS NULL
   OR (ScopeKey = ''host'' AND Code = ''host-administrator'' AND IsSuperAdministrator = 0);');

IF EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.fn_identity_role')
      AND name = N'IsSuperAdministrator'
      AND is_nullable = 1
)
    EXEC(N'ALTER TABLE dbo.fn_identity_role ALTER COLUMN IsSuperAdministrator bit NOT NULL;');

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints AS defaultObject
    INNER JOIN sys.columns AS columnObject
        ON columnObject.object_id = defaultObject.parent_object_id
       AND columnObject.column_id = defaultObject.parent_column_id
    WHERE defaultObject.parent_object_id = OBJECT_ID(N'dbo.fn_identity_role')
      AND columnObject.name = N'IsSuperAdministrator'
)
    ALTER TABLE dbo.fn_identity_role
        ADD CONSTRAINT DF_fn_identity_role_IsSuperAdministrator
        DEFAULT (0) FOR IsSuperAdministrator;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_identity_role')
      AND name = N'CK_fn_identity_role_SuperAdministratorScope'
)
    EXEC(N'
    ALTER TABLE dbo.fn_identity_role WITH CHECK
        ADD CONSTRAINT CK_fn_identity_role_SuperAdministratorScope
        CHECK
        (
            IsSuperAdministrator = 0
            OR (IsSystem = 1 AND TenantId IS NULL AND ScopeKey = ''host'')
        );');
