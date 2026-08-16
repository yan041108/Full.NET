-- 092：Messaging 模块 B0 域内同事务审计。
CREATE TABLE IF NOT EXISTS fn_messaging_domain_audit (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    ActionKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '操作键',
    EntityId BINARY(16) NOT NULL COMMENT '实体标识',
    Outcome varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '结果',
    ActorUserId BINARY(16) NULL COMMENT '操作者用户标识',
    ActorDisplayName varchar(128) NULL COMMENT '操作者显示名',
    TraceId varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '追踪标识',
    DiffSummaryJson text NULL COMMENT '差异摘要(JSON)',
    OccurredAtUtc datetime(6) NOT NULL COMMENT '发生时间(UTC)',
    CONSTRAINT PK_fn_messaging_domain_audit PRIMARY KEY (Id),
    CONSTRAINT CK_fn_messaging_domain_audit_Outcome
        CHECK (Outcome IN ('success', 'failure')),
    KEY IX_fn_messaging_domain_audit_OccurredAtUtc_Id (OccurredAtUtc, Id)
) COMMENT='消息投递领域审计表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
