-- 与订阅写入同事务保留容量，防止并发创建越过预算。
IF OBJECT_ID(N'dbo.fn_webhooks_fanout_budget', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_webhooks_fanout_budget (
        TenantId uniqueidentifier NOT NULL,
        EventType nvarchar(128) NOT NULL,
        UsedCount int NOT NULL,
        CONSTRAINT pk_fn_webhooks_fanout_budget PRIMARY KEY CLUSTERED (TenantId, EventType),
        CONSTRAINT ck_fn_webhooks_fanout_budget_used CHECK (UsedCount >= 0)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_fanout_budget')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Webhook 事件流扇出容量', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_fanout_budget';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_fanout_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_fanout_budget'), N'EventType', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'事件类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_fanout_budget', @level2type=N'COLUMN', @level2name=N'EventType';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_fanout_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_fanout_budget'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_fanout_budget', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_webhooks_fanout_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_fanout_budget'), N'UsedCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已保留订阅数量', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_webhooks_fanout_budget', @level2type=N'COLUMN', @level2name=N'UsedCount';
END;
-- 回滚保留预算与订阅事实；恢复旧实现后可单独删除此派生表。

-- 建表后回填失败也必须可恢复；该派生计数在迁移维护窗口按订阅事实重建。
INSERT INTO dbo.fn_webhooks_fanout_budget (TenantId, EventType, UsedCount)
SELECT source.TenantId, source.EventType, source.UsedCount
FROM (SELECT TenantId, EventType, COUNT(1) AS UsedCount FROM dbo.fn_webhooks_subscription GROUP BY TenantId, EventType) AS source
WHERE NOT EXISTS (SELECT 1 FROM dbo.fn_webhooks_fanout_budget AS target WITH (UPDLOCK,HOLDLOCK)
    WHERE target.TenantId=source.TenantId AND target.EventType=source.EventType);
UPDATE dbo.fn_webhooks_fanout_budget SET UsedCount=COALESCE(source.UsedCount,0)
FROM dbo.fn_webhooks_fanout_budget
LEFT JOIN (SELECT TenantId, EventType, COUNT(1) AS UsedCount FROM dbo.fn_webhooks_subscription GROUP BY TenantId, EventType) AS source
    ON fn_webhooks_fanout_budget.TenantId=source.TenantId AND fn_webhooks_fanout_budget.EventType=source.EventType
WHERE fn_webhooks_fanout_budget.UsedCount<>COALESCE(source.UsedCount,0);
