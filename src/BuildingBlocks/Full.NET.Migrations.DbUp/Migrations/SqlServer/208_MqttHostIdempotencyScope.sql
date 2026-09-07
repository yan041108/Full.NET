-- 208：Host 空租户幂等唯一索引。持久化 ScopeTenantKey 哨兵，并以过滤唯一索引覆盖可空幂等键。
-- 升级时停止旧 API 后再应用。

IF NOT EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.fn_mqtt_message')
      AND name = N'ScopeTenantKey')
    ALTER TABLE dbo.fn_mqtt_message
        ADD ScopeTenantKey AS (ISNULL(TenantId, CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000'))) PERSISTED;

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.fn_mqtt_message')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_mqtt_message'), N'ScopeTenantKey', 'ColumnId')
      AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',
        @value=N'作用域租户键；Host 空租户使用全零哨兵以进入唯一索引',
        @level0type=N'SCHEMA', @level0name=N'dbo',
        @level1type=N'TABLE', @level1name=N'fn_mqtt_message',
        @level2type=N'COLUMN', @level2name=N'ScopeTenantKey';

IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_mqtt_message')
      AND name = N'UX_fn_mqtt_message_TenantId_IdempotencyKey')
    DROP INDEX UX_fn_mqtt_message_TenantId_IdempotencyKey ON dbo.fn_mqtt_message;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_mqtt_message')
      AND name = N'UX_fn_mqtt_message_ScopeTenantKey_IdempotencyKey')
    CREATE UNIQUE INDEX UX_fn_mqtt_message_ScopeTenantKey_IdempotencyKey
        ON dbo.fn_mqtt_message(ScopeTenantKey, IdempotencyKey)
        WHERE IdempotencyKey IS NOT NULL;
