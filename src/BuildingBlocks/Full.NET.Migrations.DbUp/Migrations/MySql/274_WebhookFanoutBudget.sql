-- 唯一主键串行化同一租户事件流的容量保留。
CREATE TABLE IF NOT EXISTS fn_webhooks_fanout_budget (
    TenantId binary(16) NOT NULL COMMENT '租户标识',
    EventType varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '事件类型',
    UsedCount int NOT NULL COMMENT '已保留订阅数量',
    CONSTRAINT pk_fn_webhooks_fanout_budget PRIMARY KEY (TenantId, EventType),
    CONSTRAINT ck_fn_webhooks_fanout_budget_used CHECK (UsedCount >= 0)
) COMMENT='Webhook 事件流扇出容量';
INSERT INTO fn_webhooks_fanout_budget (TenantId, EventType, UsedCount)
SELECT TenantId, EventType, COUNT(1) FROM fn_webhooks_subscription GROUP BY TenantId, EventType
ON DUPLICATE KEY UPDATE UsedCount = fn_webhooks_fanout_budget.UsedCount;
-- 维护窗口从事实重建派生值；包含已无订阅的事件流，重放不能保留错误容量。
UPDATE fn_webhooks_fanout_budget AS target
LEFT JOIN (SELECT TenantId, EventType, COUNT(1) AS UsedCount FROM fn_webhooks_subscription GROUP BY TenantId, EventType) AS source
    ON target.TenantId = source.TenantId AND target.EventType = source.EventType
SET target.UsedCount = COALESCE(source.UsedCount, 0)
WHERE target.UsedCount <> COALESCE(source.UsedCount, 0);
-- 回滚保留预算与订阅事实；恢复旧实现后可单独删除此派生表。
