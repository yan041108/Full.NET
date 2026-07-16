IF OBJECT_ID(N'dbo.fn_tenant_tenant', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_tenant_tenant
    (
        Id uniqueidentifier NOT NULL PRIMARY KEY,
        Identifier nvarchar(64) NOT NULL,
        Name nvarchar(128) NOT NULL,
        Domain nvarchar(255) NOT NULL,
        IsActive bit NOT NULL,
        CreatedAt datetimeoffset(7) NOT NULL,
        UpdatedAt datetimeoffset(7) NULL,
        Version int NOT NULL CONSTRAINT DF_fn_tenant_tenant_Version DEFAULT (1)
    );
    CREATE UNIQUE INDEX UX_fn_tenant_tenant_Identifier ON dbo.fn_tenant_tenant(Identifier);
    CREATE UNIQUE INDEX UX_fn_tenant_tenant_Domain ON dbo.fn_tenant_tenant(Domain);
END;

IF OBJECT_ID(N'dbo.fn_outbox_message', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_outbox_message
    (
        Id uniqueidentifier NOT NULL PRIMARY KEY,
        Type nvarchar(256) NOT NULL,
        SchemaVersion int NOT NULL,
        ContentType nvarchar(128) NOT NULL,
        TenantId uniqueidentifier NULL,
        TraceId varchar(32) NULL,
        Payload varbinary(max) NOT NULL,
        OccurredAt datetimeoffset(7) NOT NULL,
        ProcessedAt datetimeoffset(7) NULL,
        NextAttemptAt datetimeoffset(7) NULL,
        Attempts int NOT NULL CONSTRAINT DF_fn_outbox_message_Attempts DEFAULT (0),
        LockId uniqueidentifier NULL,
        LockedUntil datetimeoffset(7) NULL,
        Error nvarchar(2000) NULL
    );
    CREATE INDEX IX_fn_outbox_message_Pending
        ON dbo.fn_outbox_message(ProcessedAt, NextAttemptAt, LockedUntil, OccurredAt);
END;
