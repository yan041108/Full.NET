-- KeyId 初始复用应用端订阅 UUID；MySQL DDL 非事务，部分完成与未记账均可恢复。
DROP PROCEDURE IF EXISTS fn_webhooks_signing_key_identity;
DELIMITER $$
CREATE PROCEDURE fn_webhooks_signing_key_identity()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_webhooks_subscription' AND COLUMN_NAME='SigningKeyId') THEN
        ALTER TABLE fn_webhooks_subscription ADD COLUMN SigningKeyId binary(16) NULL COMMENT '当前签名密钥的公开标识；秘密仍受保护保存';
    END IF;
    UPDATE fn_webhooks_subscription SET SigningKeyId=Id WHERE SigningKeyId IS NULL;
    IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_webhooks_subscription' AND COLUMN_NAME='SigningKeyId' AND IS_NULLABLE='YES') THEN
        ALTER TABLE fn_webhooks_subscription MODIFY COLUMN SigningKeyId binary(16) NOT NULL COMMENT '当前签名密钥的公开标识；秘密仍受保护保存';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_webhooks_subscription' AND INDEX_NAME='UX_fn_webhooks_subscription_SigningKeyId') THEN
        CREATE UNIQUE INDEX UX_fn_webhooks_subscription_SigningKeyId ON fn_webhooks_subscription(SigningKeyId);
    END IF;
END$$
DELIMITER ;
CALL fn_webhooks_signing_key_identity();
DROP PROCEDURE IF EXISTS fn_webhooks_signing_key_identity;
