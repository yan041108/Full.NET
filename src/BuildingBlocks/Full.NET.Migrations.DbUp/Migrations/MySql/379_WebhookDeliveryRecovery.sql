-- 表与操作键约束在同一条原子 DDL 中创建，未记账重跑保留已提交的回执。
CREATE TABLE IF NOT EXISTS fn_webhooks_delivery_recovery (
    Id binary(16) NOT NULL COMMENT '应用端 UUID v7 回执主键',
    TenantId binary(16) NOT NULL COMMENT '可信租户标识',
    OperationId binary(16) NOT NULL COMMENT '固定操作标识',
    ActorUserId binary(16) NOT NULL COMMENT '提交确认的操作者',
    ActorSessionId binary(16) NOT NULL COMMENT '提交确认时的会话',
    SubscriptionId binary(16) NOT NULL COMMENT '原订阅标识',
    DeliveryId binary(16) NOT NULL COMMENT '原投递标识',
    EventId binary(16) NOT NULL COMMENT '原事件标识',
    PreviousVersion int NOT NULL COMMENT '对账前投递修订',
    Version int NOT NULL COMMENT '确认后投递修订',
    AttemptCount int NOT NULL COMMENT '发送方原实际尝试次数',
    PayloadSha256 char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '原 HTTP 请求体 SHA-256',
    ReceiverReceiptSha256 char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '权威接收方证据原始字节 SHA-256',
    ConfirmedAtUtc datetime(6) NOT NULL COMMENT '确认提交时间 UTC',
    CONSTRAINT PK_fn_webhooks_delivery_recovery PRIMARY KEY (Id),
    CONSTRAINT UX_fn_webhooks_delivery_recovery_TenantOperation UNIQUE (TenantId,OperationId),
    CONSTRAINT CK_fn_webhooks_delivery_recovery_Version CHECK (PreviousVersion>=1 AND Version=PreviousVersion+1 AND AttemptCount>=0)
) COMMENT='接收方已提交投递的恢复确认和同事务审计' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
