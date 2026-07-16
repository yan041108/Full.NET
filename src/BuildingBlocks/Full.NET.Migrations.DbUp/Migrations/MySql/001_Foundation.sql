CREATE TABLE IF NOT EXISTS fn_tenant_tenant
(
    Id char(36) NOT NULL PRIMARY KEY,
    Identifier varchar(64) NOT NULL,
    Name varchar(128) NOT NULL,
    Domain varchar(255) NOT NULL,
    IsActive boolean NOT NULL,
    CreatedAt datetime(6) NOT NULL,
    UpdatedAt datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    UNIQUE KEY UX_fn_tenant_tenant_Identifier (Identifier),
    UNIQUE KEY UX_fn_tenant_tenant_Domain (Domain)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_outbox_message
(
    Id char(36) NOT NULL PRIMARY KEY,
    Type varchar(256) NOT NULL,
    SchemaVersion int NOT NULL,
    ContentType varchar(128) NOT NULL,
    TenantId char(36) NULL,
    TraceId varchar(32) NULL,
    Payload longblob NOT NULL,
    OccurredAt datetime(6) NOT NULL,
    ProcessedAt datetime(6) NULL,
    NextAttemptAt datetime(6) NULL,
    Attempts int NOT NULL DEFAULT 0,
    LockId char(36) NULL,
    LockedUntil datetime(6) NULL,
    Error varchar(2000) NULL,
    KEY IX_fn_outbox_message_Pending (ProcessedAt, NextAttemptAt, LockedUntil, OccurredAt)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
