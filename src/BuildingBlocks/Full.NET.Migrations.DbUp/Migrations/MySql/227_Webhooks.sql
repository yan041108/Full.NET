-- 227: webhook subscriptions and delivery attempts.
CREATE TABLE IF NOT EXISTS fn_webhooks_subscription (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NOT NULL COMMENT '租户标识',
    EventType varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '事件类型',
    TargetUrl varchar(2048) NOT NULL COMMENT '回调 URL',
    SigningSecretHash varchar(1024) NOT NULL COMMENT '受保护签名密钥',
    IsActive tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_webhooks_subscription PRIMARY KEY (Id),
    KEY IX_fn_webhooks_subscription_TenantEvent (TenantId, EventType)
) COMMENT='Webhook 订阅表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_webhooks_delivery (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    SubscriptionId BINARY(16) NOT NULL COMMENT '订阅标识',
    EventId BINARY(16) NOT NULL COMMENT '事件标识',
    PayloadBody longtext NOT NULL COMMENT '载荷正文',
    PayloadDigest varchar(128) NOT NULL COMMENT '载荷摘要',
    Status varchar(32) NOT NULL COMMENT '投递状态',
    AttemptCount int NOT NULL DEFAULT 0 COMMENT '尝试次数',
    NextAttemptAtUtc datetime(6) NULL COMMENT '下次重试(UTC)',
    LastError varchar(512) NULL COMMENT '最近错误',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_webhooks_delivery PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_webhooks_delivery_EventSubscription (SubscriptionId, EventId),
    KEY IX_fn_webhooks_delivery_Pending (Status, NextAttemptAtUtc, CreatedAtUtc)
) COMMENT='Webhook 投递表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
