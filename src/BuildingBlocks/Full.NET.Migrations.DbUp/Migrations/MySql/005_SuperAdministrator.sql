-- 为 Identity 角色增加超级管理员标记并回填受保护系统角色；本迁移不删除数据。
-- MySQL DDL 会隐式提交且无法整体回滚，因此每一步都必须能在 DbUp 未记账的半完成状态下重跑。
SET @super_admin_ddl = IF(
    EXISTS(
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_identity_role'
          AND COLUMN_NAME = 'IsSuperAdministrator'),
    'SELECT 1',
    'ALTER TABLE fn_identity_role ADD COLUMN IsSuperAdministrator boolean NULL');
PREPARE super_admin_statement FROM @super_admin_ddl;
EXECUTE super_admin_statement;
DEALLOCATE PREPARE super_admin_statement;

UPDATE fn_identity_role
SET IsSuperAdministrator = CASE
    WHEN ScopeKey = 'host' AND Code = 'host-administrator' THEN true
    ELSE false
END
WHERE IsSuperAdministrator IS NULL
   OR (ScopeKey = 'host' AND Code = 'host-administrator' AND IsSuperAdministrator = false);

ALTER TABLE fn_identity_role
    MODIFY COLUMN IsSuperAdministrator boolean NOT NULL DEFAULT false;

SET @super_admin_ddl = IF(
    EXISTS(
        SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_identity_role'
          AND CONSTRAINT_NAME = 'CK_fn_identity_role_SuperAdministratorScope'),
    'SELECT 1',
    'ALTER TABLE fn_identity_role ADD CONSTRAINT CK_fn_identity_role_SuperAdministratorScope CHECK (IsSuperAdministrator = false OR (IsSystem = true AND TenantId IS NULL AND ScopeKey = ''host''))');
PREPARE super_admin_statement FROM @super_admin_ddl;
EXECUTE super_admin_statement;
DEALLOCATE PREPARE super_admin_statement;
