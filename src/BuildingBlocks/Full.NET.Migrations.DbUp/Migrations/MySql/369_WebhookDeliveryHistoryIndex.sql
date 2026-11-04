-- 投递历史在同一订阅内按入队时间和标识分页；只补索引，不改正文、状态或签名材料。
-- MySQL DDL 非事务；索引已完成但 DbUp 未记账时探测收敛，回退可移除索引且保留全部业务数据。
DROP PROCEDURE IF EXISTS fn_webhooks_delivery_history_index;
DELIMITER $$
CREATE PROCEDURE fn_webhooks_delivery_history_index()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_webhooks_delivery' AND INDEX_NAME='IX_fn_webhooks_delivery_SubscriptionCreatedId') THEN
        CREATE INDEX IX_fn_webhooks_delivery_SubscriptionCreatedId ON fn_webhooks_delivery(SubscriptionId,CreatedAtUtc,Id);
    END IF;
END$$
DELIMITER ;
CALL fn_webhooks_delivery_history_index();
DROP PROCEDURE IF EXISTS fn_webhooks_delivery_history_index;
