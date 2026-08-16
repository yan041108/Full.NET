CREATE TABLE IF NOT EXISTS fn_tenant_tenant (
    Id char(36) NOT NULL PRIMARY KEY COMMENT '逻辑主键',
    Identifier varchar(64) NOT NULL COMMENT '唯一标识符',
    Name varchar(128) NOT NULL COMMENT '名称',
    Domain varchar(255) NOT NULL COMMENT '域名',
    IsActive boolean NOT NULL COMMENT '是否启用',
    CreatedAt datetime(6) NOT NULL COMMENT '创建时间',
    UpdatedAt datetime(6) NULL COMMENT '更新时间',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    UNIQUE KEY UX_fn_tenant_tenant_Identifier (Identifier),
    UNIQUE KEY UX_fn_tenant_tenant_Domain (Domain)
) COMMENT='租户租户表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_outbox_message (
    Id char(36) NOT NULL PRIMARY KEY COMMENT '逻辑主键',
    Type varchar(256) NOT NULL COMMENT '类型',
    SchemaVersion int NOT NULL COMMENT 'Schema 版本',
    ContentType varchar(128) NOT NULL COMMENT '内容类型',
    TenantId char(36) NULL COMMENT '租户标识；NULL 表示 Host 级',
    TraceId varchar(32) NULL COMMENT '追踪标识',
    Payload longblob NOT NULL COMMENT '消息正文',
    OccurredAt datetime(6) NOT NULL COMMENT '发生时间',
    ProcessedAt datetime(6) NULL COMMENT '处理完成时间',
    NextAttemptAt datetime(6) NULL COMMENT '下次重试时间',
    Attempts int NOT NULL DEFAULT 0 COMMENT '重试次数',
    LockId char(36) NULL COMMENT '锁标识',
    LockedUntil datetime(6) NULL COMMENT '锁定截止时间',
    Error varchar(2000) NULL COMMENT '错误信息',
    KEY IX_fn_outbox_message_Pending (ProcessedAt, NextAttemptAt, LockedUntil, OccurredAt)
) COMMENT='事务发件箱消息，承载待发布的集成事件' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
