-- 投递历史在同一订阅内按入队时间和标识分页；只补索引，不改正文、状态或签名材料。
-- 索引创建已完成但 DbUp 未记账时可重入，回退可移除此索引且保留全部业务数据。
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_webhooks_delivery') AND name=N'IX_fn_webhooks_delivery_SubscriptionCreatedId')
    CREATE INDEX IX_fn_webhooks_delivery_SubscriptionCreatedId ON dbo.fn_webhooks_delivery(SubscriptionId,CreatedAtUtc,Id);
