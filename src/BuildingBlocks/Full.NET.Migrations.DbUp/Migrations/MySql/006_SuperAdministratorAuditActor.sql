-- MySQL DDL 会隐式提交；列和索引分别收敛，允许 DbUp 未记账的半完成状态安全重跑。
SET @super_admin_audit_ddl = IF(
    EXISTS(
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_identity_auth_audit'
          AND COLUMN_NAME = 'ActorUserId'),
    'SELECT 1',
    'ALTER TABLE fn_identity_auth_audit ADD COLUMN ActorUserId char(36) NULL');
PREPARE super_admin_audit_statement FROM @super_admin_audit_ddl;
EXECUTE super_admin_audit_statement;
DEALLOCATE PREPARE super_admin_audit_statement;

SET @super_admin_audit_ddl = IF(
    EXISTS(
        SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_identity_auth_audit'
          AND INDEX_NAME = 'IX_fn_identity_auth_audit_EventType_OccurredAt'),
    'SELECT 1',
    'CREATE INDEX IX_fn_identity_auth_audit_EventType_OccurredAt ON fn_identity_auth_audit(EventType, OccurredAtUtc DESC)');
PREPARE super_admin_audit_statement FROM @super_admin_audit_ddl;
EXECUTE super_admin_audit_statement;
DEALLOCATE PREPARE super_admin_audit_statement;
