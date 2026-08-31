-- 105：为现有站内信表补齐可信 Scope/Intent，并收紧 RecipientEndpoint 验证状态。
-- MySQL 用 INFORMATION_SCHEMA 探测后幂等补列；UNIQUE 允许多条 IntentId 为空的手工发信，与 SQL Server 过滤唯一索引语义对齐。
DROP PROCEDURE IF EXISTS fn_notifications_inbox_scope_extension;
DELIMITER $$
CREATE PROCEDURE fn_notifications_inbox_scope_extension()
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_notifications_inbox_message'
          AND COLUMN_NAME = 'ScopeKey') THEN
        ALTER TABLE fn_notifications_inbox_message
            ADD COLUMN ScopeKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'host' COMMENT '作用域键';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_notifications_inbox_message'
          AND COLUMN_NAME = 'TenantScopeKey') THEN
        ALTER TABLE fn_notifications_inbox_message
            ADD COLUMN TenantScopeKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL DEFAULT 'host' COMMENT '租户作用域唯一键';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_notifications_inbox_message'
          AND COLUMN_NAME = 'IntentId') THEN
        ALTER TABLE fn_notifications_inbox_message
            ADD COLUMN IntentId BINARY(16) NULL COMMENT '通知意图标识';
    END IF;

    UPDATE fn_notifications_inbox_message
    SET ScopeKey = 'host',
        TenantScopeKey = 'host'
    WHERE TenantId IS NULL
      AND (ScopeKey <> 'host' OR TenantScopeKey <> 'host');

    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_notifications_inbox_message'
          AND CONSTRAINT_NAME = 'CK_fn_notifications_inbox_message_ScopeKey') THEN
        ALTER TABLE fn_notifications_inbox_message
            ADD CONSTRAINT CK_fn_notifications_inbox_message_ScopeKey
            CHECK (ScopeKey IN ('host', 'tenant'));
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_notifications_inbox_message'
          AND CONSTRAINT_NAME = 'FK_fn_notifications_inbox_message_Intent') THEN
        ALTER TABLE fn_notifications_inbox_message
            ADD CONSTRAINT FK_fn_notifications_inbox_message_Intent
            FOREIGN KEY (IntentId) REFERENCES fn_notifications_intent(Id);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_notifications_inbox_message'
          AND INDEX_NAME = 'UX_fn_notifications_inbox_Intent_Recipient') THEN
        ALTER TABLE fn_notifications_inbox_message
            ADD UNIQUE INDEX UX_fn_notifications_inbox_Intent_Recipient (TenantScopeKey, IntentId, RecipientUserId);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_notifications_inbox_message'
          AND INDEX_NAME = 'IX_fn_notifications_inbox_Scope_Unread') THEN
        ALTER TABLE fn_notifications_inbox_message
            ADD INDEX IX_fn_notifications_inbox_Scope_Unread (TenantScopeKey, RecipientUserId, Status, Id);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_notifications_recipient_endpoint'
          AND CONSTRAINT_NAME = 'CK_fn_notifications_endpoint_Verification') THEN
        ALTER TABLE fn_notifications_recipient_endpoint
            ADD CONSTRAINT CK_fn_notifications_endpoint_Verification
            CHECK (VerificationStatusKey IN ('pending', 'verified', 'failed'));
    END IF;
END$$
DELIMITER ;

CALL fn_notifications_inbox_scope_extension();
DROP PROCEDURE fn_notifications_inbox_scope_extension;
