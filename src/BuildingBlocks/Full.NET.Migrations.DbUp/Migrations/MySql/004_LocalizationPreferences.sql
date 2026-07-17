-- MySQL DDL 会隐式提交；ADD 使用 INFORMATION_SCHEMA 条件执行，UPDATE/MODIFY 负责最终收敛。
SET @localization_ddl = IF(
    EXISTS(
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_identity_user'
          AND COLUMN_NAME = 'PreferredLocale'),
    'SELECT 1',
    'ALTER TABLE fn_identity_user ADD COLUMN PreferredLocale varchar(35) NULL');
PREPARE localization_statement FROM @localization_ddl;
EXECUTE localization_statement;
DEALLOCATE PREPARE localization_statement;
UPDATE fn_identity_user SET PreferredLocale = 'zh-CN' WHERE PreferredLocale IS NULL;
ALTER TABLE fn_identity_user MODIFY COLUMN PreferredLocale varchar(35) NOT NULL DEFAULT 'zh-CN';

SET @localization_ddl = IF(
    EXISTS(
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_identity_user'
          AND COLUMN_NAME = 'ProfileVersion'),
    'SELECT 1',
    'ALTER TABLE fn_identity_user ADD COLUMN ProfileVersion int NULL');
PREPARE localization_statement FROM @localization_ddl;
EXECUTE localization_statement;
DEALLOCATE PREPARE localization_statement;
UPDATE fn_identity_user SET ProfileVersion = 1 WHERE ProfileVersion IS NULL;
ALTER TABLE fn_identity_user MODIFY COLUMN ProfileVersion int NOT NULL DEFAULT 1;

SET @localization_ddl = IF(
    EXISTS(
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_tenant_tenant'
          AND COLUMN_NAME = 'DefaultLocale'),
    'SELECT 1',
    'ALTER TABLE fn_tenant_tenant ADD COLUMN DefaultLocale varchar(35) NULL');
PREPARE localization_statement FROM @localization_ddl;
EXECUTE localization_statement;
DEALLOCATE PREPARE localization_statement;
UPDATE fn_tenant_tenant SET DefaultLocale = 'zh-CN' WHERE DefaultLocale IS NULL;
ALTER TABLE fn_tenant_tenant MODIFY COLUMN DefaultLocale varchar(35) NOT NULL DEFAULT 'zh-CN';
