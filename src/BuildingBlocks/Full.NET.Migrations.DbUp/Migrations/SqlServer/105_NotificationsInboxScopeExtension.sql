-- 105：为现有站内信表补齐可信 Scope/Intent，并收紧 RecipientEndpoint 验证状态。
-- 不重建第二套 Inbox；存量 Host 行回填 scope=host。Intent 幂等使用过滤唯一索引，允许多条 IntentId 为空的手工发信。
-- SQL Server 同一批次无法编译尚未提交的新列引用，因此 UPDATE/CHECK/FK/索引必须走动态 SQL。
IF COL_LENGTH(N'dbo.fn_notifications_inbox_message', N'ScopeKey') IS NULL
    ALTER TABLE dbo.fn_notifications_inbox_message
        ADD ScopeKey varchar(16) NOT NULL
            CONSTRAINT DF_fn_notifications_inbox_message_ScopeKey DEFAULT ('host');

IF COL_LENGTH(N'dbo.fn_notifications_inbox_message', N'TenantScopeKey') IS NULL
    ALTER TABLE dbo.fn_notifications_inbox_message
        ADD TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL
            CONSTRAINT DF_fn_notifications_inbox_message_TenantScopeKey DEFAULT (N'host');

IF COL_LENGTH(N'dbo.fn_notifications_inbox_message', N'IntentId') IS NULL
    ALTER TABLE dbo.fn_notifications_inbox_message
        ADD IntentId uniqueidentifier NULL;

EXEC sys.sp_executesql N'
UPDATE dbo.fn_notifications_inbox_message
SET ScopeKey = ''host'',
    TenantScopeKey = N''host''
WHERE TenantId IS NULL
  AND (ScopeKey <> ''host'' OR TenantScopeKey <> N''host'');
';

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message')
      AND name = N'CK_fn_notifications_inbox_message_ScopeKey')
    EXEC sys.sp_executesql N'
ALTER TABLE dbo.fn_notifications_inbox_message
    ADD CONSTRAINT CK_fn_notifications_inbox_message_ScopeKey
    CHECK (ScopeKey IN (''host'', ''tenant''));
';

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message')
      AND name = N'FK_fn_notifications_inbox_message_Intent')
    EXEC sys.sp_executesql N'
ALTER TABLE dbo.fn_notifications_inbox_message
    ADD CONSTRAINT FK_fn_notifications_inbox_message_Intent
    FOREIGN KEY (IntentId) REFERENCES dbo.fn_notifications_intent(Id);
';

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message')
      AND name = N'UX_fn_notifications_inbox_Intent_Recipient')
    EXEC sys.sp_executesql N'
CREATE UNIQUE NONCLUSTERED INDEX UX_fn_notifications_inbox_Intent_Recipient
    ON dbo.fn_notifications_inbox_message(TenantScopeKey, IntentId, RecipientUserId)
    WHERE IntentId IS NOT NULL;
';

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message')
      AND name = N'IX_fn_notifications_inbox_Scope_Unread')
    EXEC sys.sp_executesql N'
CREATE NONCLUSTERED INDEX IX_fn_notifications_inbox_Scope_Unread
    ON dbo.fn_notifications_inbox_message(TenantScopeKey, RecipientUserId, Status, Id);
';

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_notifications_recipient_endpoint')
      AND name = N'CK_fn_notifications_endpoint_Verification')
    ALTER TABLE dbo.fn_notifications_recipient_endpoint
        ADD CONSTRAINT CK_fn_notifications_endpoint_Verification
        CHECK (VerificationStatusKey IN ('pending', 'verified', 'failed'));

IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_inbox_message'), N'ScopeKey', 'ColumnId')
      AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_inbox_message', @level2type=N'COLUMN', @level2name=N'ScopeKey';

IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_inbox_message'), N'TenantScopeKey', 'ColumnId')
      AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户作用域唯一键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_inbox_message', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';

IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_notifications_inbox_message')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_inbox_message'), N'IntentId', 'ColumnId')
      AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知意图标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_inbox_message', @level2type=N'COLUMN', @level2name=N'IntentId';
