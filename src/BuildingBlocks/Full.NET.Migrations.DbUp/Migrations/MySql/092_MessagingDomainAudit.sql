-- 092：Messaging 模块 B0 域内同事务审计。
CREATE TABLE IF NOT EXISTS fn_messaging_domain_audit
(
    Id BINARY(16) NOT NULL,
    TenantId BINARY(16) NULL,
    ActionKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    EntityId BINARY(16) NOT NULL,
    Outcome varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    ActorUserId BINARY(16) NULL,
    ActorDisplayName varchar(128) NULL,
    TraceId varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL,
    DiffSummaryJson text NULL,
    OccurredAtUtc datetime(6) NOT NULL,
    CONSTRAINT PK_fn_messaging_domain_audit PRIMARY KEY (Id),
    CONSTRAINT CK_fn_messaging_domain_audit_Outcome
        CHECK (Outcome IN ('success', 'failure')),
    KEY IX_fn_messaging_domain_audit_OccurredAtUtc_Id (OccurredAtUtc, Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
