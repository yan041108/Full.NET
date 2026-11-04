-- 回执与原投递同事务，保留原事件和历史次数；不建立跨模块外键。
-- MySQL DDL 非事务；表与唯一索引分别探测，失败恢复不删除已提交回执。
CREATE TABLE IF NOT EXISTS fn_webhooks_delivery_replay (
    Id binary(16) NOT NULL COMMENT '应用端UUID v7回执主键',
    TenantId binary(16) NOT NULL COMMENT '可信企业标识',
    OperationId binary(16) NOT NULL COMMENT '客户端固定操作标识，重试不得替换',
    ActorUserId binary(16) NOT NULL COMMENT '实际操作者账号标识',
    ActorSessionId binary(16) NOT NULL COMMENT '提交时的原交互会话标识',
    SubscriptionId binary(16) NOT NULL COMMENT '原订阅标识',
    DeliveryId binary(16) NOT NULL COMMENT '原投递标识，不新建投递',
    EventId binary(16) NOT NULL COMMENT '保持不变的原业务事件身份',
    PreviousVersion int NOT NULL COMMENT '排队前终态失败投递修订',
    Version int NOT NULL COMMENT '排队成功后的投递修订',
    AttemptCount int NOT NULL COMMENT '排队时原已完成次数，不重置',
    QueuedAtUtc datetime(6) NOT NULL COMMENT '排队事务的UTC时间',
    CONSTRAINT PK_fn_webhooks_delivery_replay PRIMARY KEY (Id),
    CONSTRAINT CK_fn_webhooks_delivery_replay_Version CHECK (PreviousVersion>=1 AND Version=PreviousVersion+1 AND AttemptCount>=0)
) COMMENT='手动重放的不可变排队回执及同事务领域审计' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
DROP PROCEDURE IF EXISTS fn_webhooks_replay_indexes;
DELIMITER $$
CREATE PROCEDURE fn_webhooks_replay_indexes()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_webhooks_delivery_replay' AND INDEX_NAME='UX_fn_webhooks_delivery_replay_TenantOperation') THEN
        CREATE UNIQUE INDEX UX_fn_webhooks_delivery_replay_TenantOperation ON fn_webhooks_delivery_replay(TenantId,OperationId);
    END IF;
END$$
DELIMITER ;
CALL fn_webhooks_replay_indexes();
DROP PROCEDURE IF EXISTS fn_webhooks_replay_indexes;
