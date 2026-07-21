-- 每一类违规都必须产生稳定规则码。
UPDATE fn_identity_user
SET DisplayName = 'x';

DELETE FROM fn_identity_user_role;

TRUNCATE TABLE fn_outbox_message;

ALTER TABLE fn_outbox_message DROP COLUMN Type;

DROP TABLE fn_tenant_tenant;

ALTER TABLE fn_identity_user RENAME COLUMN Username TO LoginName;
