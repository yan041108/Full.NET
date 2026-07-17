ALTER TABLE fn_identity_user ADD COLUMN PreferredLocale varchar(35) NULL;
UPDATE fn_identity_user SET PreferredLocale = 'zh-CN' WHERE PreferredLocale IS NULL;
ALTER TABLE fn_identity_user MODIFY COLUMN PreferredLocale varchar(35) NOT NULL DEFAULT 'zh-CN';

ALTER TABLE fn_identity_user ADD COLUMN ProfileVersion int NULL;
UPDATE fn_identity_user SET ProfileVersion = 1 WHERE ProfileVersion IS NULL;
ALTER TABLE fn_identity_user MODIFY COLUMN ProfileVersion int NOT NULL DEFAULT 1;

ALTER TABLE fn_tenant_tenant ADD COLUMN DefaultLocale varchar(35) NULL;
UPDATE fn_tenant_tenant SET DefaultLocale = 'zh-CN' WHERE DefaultLocale IS NULL;
ALTER TABLE fn_tenant_tenant MODIFY COLUMN DefaultLocale varchar(35) NOT NULL DEFAULT 'zh-CN';
