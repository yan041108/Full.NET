-- 010 Expand：新增规范 Tenancy 表与 Outbox 镜像列；legacy 对象保持可用。
IF OBJECT_ID(N'dbo.fn_tenancy_tenant', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_tenancy_tenant
    (
        Id uniqueidentifier NOT NULL,
        Identifier nvarchar(64) NOT NULL,
        Name nvarchar(128) NOT NULL,
        Domain nvarchar(255) NOT NULL,
        IsActive bit NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        DefaultLocale varchar(35) NOT NULL,
        Version int NOT NULL CONSTRAINT DF_fn_tenancy_tenant_Version DEFAULT (1),
        CONSTRAINT PK_fn_tenancy_tenant PRIMARY KEY CLUSTERED (Id)
    );
    CREATE UNIQUE INDEX UX_fn_tenancy_tenant_Identifier ON dbo.fn_tenancy_tenant(Identifier);
    CREATE UNIQUE INDEX UX_fn_tenancy_tenant_Domain ON dbo.fn_tenancy_tenant(Domain);
END;

IF EXISTS
(
    SELECT 1
    FROM dbo.fn_tenant_tenant AS legacy
    INNER JOIN dbo.fn_tenancy_tenant AS canonical ON canonical.Id = legacy.Id
    WHERE canonical.Identifier <> legacy.Identifier
       OR canonical.Name <> legacy.Name
       OR canonical.Domain <> legacy.Domain
       OR canonical.IsActive <> legacy.IsActive
       OR canonical.CreatedAtUtc <> legacy.CreatedAt
       OR (canonical.UpdatedAtUtc IS NULL AND legacy.UpdatedAt IS NOT NULL)
       OR (canonical.UpdatedAtUtc IS NOT NULL AND legacy.UpdatedAt IS NULL)
       OR (canonical.UpdatedAtUtc IS NOT NULL AND legacy.UpdatedAt IS NOT NULL
           AND canonical.UpdatedAtUtc <> legacy.UpdatedAt)
       OR canonical.DefaultLocale <> legacy.DefaultLocale
       OR canonical.Version <> legacy.Version
)
    THROW 50000, 'Tenant naming conflict: fn_tenancy_tenant count=1', 1;

INSERT INTO dbo.fn_tenancy_tenant
    (Id, Identifier, Name, Domain, IsActive, CreatedAtUtc, UpdatedAtUtc, DefaultLocale, Version)
SELECT legacy.Id,
       legacy.Identifier,
       legacy.Name,
       legacy.Domain,
       legacy.IsActive,
       legacy.CreatedAt,
       legacy.UpdatedAt,
       legacy.DefaultLocale,
       legacy.Version
FROM dbo.fn_tenant_tenant AS legacy
WHERE NOT EXISTS
(
    SELECT 1 FROM dbo.fn_tenancy_tenant AS canonical WHERE canonical.Id = legacy.Id
);

IF COL_LENGTH(N'dbo.fn_outbox_message', N'MessageType') IS NULL
    ALTER TABLE dbo.fn_outbox_message ADD MessageType nvarchar(256) NULL;
IF COL_LENGTH(N'dbo.fn_outbox_message', N'OccurredAtUtc') IS NULL
    ALTER TABLE dbo.fn_outbox_message ADD OccurredAtUtc datetimeoffset(7) NULL;
IF COL_LENGTH(N'dbo.fn_outbox_message', N'ProcessedAtUtc') IS NULL
    ALTER TABLE dbo.fn_outbox_message ADD ProcessedAtUtc datetimeoffset(7) NULL;
IF COL_LENGTH(N'dbo.fn_outbox_message', N'NextAttemptAtUtc') IS NULL
    ALTER TABLE dbo.fn_outbox_message ADD NextAttemptAtUtc datetimeoffset(7) NULL;
IF COL_LENGTH(N'dbo.fn_outbox_message', N'LockedUntilUtc') IS NULL
    ALTER TABLE dbo.fn_outbox_message ADD LockedUntilUtc datetimeoffset(7) NULL;

EXEC(N'
IF EXISTS
(
    SELECT 1
    FROM dbo.fn_outbox_message
    WHERE MessageType IS NOT NULL
      AND [Type] IS NOT NULL
      AND MessageType <> [Type]
)
    THROW 50000, ''Outbox naming conflict: MessageType count=1'', 1;

IF EXISTS
(
    SELECT 1 FROM dbo.fn_outbox_message
    WHERE OccurredAtUtc IS NOT NULL AND OccurredAt IS NOT NULL AND OccurredAtUtc <> OccurredAt
)
    THROW 50000, ''Outbox naming conflict: OccurredAtUtc count=1'', 1;

IF EXISTS
(
    SELECT 1 FROM dbo.fn_outbox_message
    WHERE ProcessedAtUtc IS NOT NULL AND ProcessedAt IS NOT NULL AND ProcessedAtUtc <> ProcessedAt
)
    THROW 50000, ''Outbox naming conflict: ProcessedAtUtc count=1'', 1;

IF EXISTS
(
    SELECT 1 FROM dbo.fn_outbox_message
    WHERE NextAttemptAtUtc IS NOT NULL AND NextAttemptAt IS NOT NULL AND NextAttemptAtUtc <> NextAttemptAt
)
    THROW 50000, ''Outbox naming conflict: NextAttemptAtUtc count=1'', 1;

IF EXISTS
(
    SELECT 1 FROM dbo.fn_outbox_message
    WHERE LockedUntilUtc IS NOT NULL AND LockedUntil IS NOT NULL AND LockedUntilUtc <> LockedUntil
)
    THROW 50000, ''Outbox naming conflict: LockedUntilUtc count=1'', 1;

UPDATE dbo.fn_outbox_message
SET MessageType = [Type]
WHERE MessageType IS NULL AND [Type] IS NOT NULL;

UPDATE dbo.fn_outbox_message
SET OccurredAtUtc = OccurredAt
WHERE OccurredAtUtc IS NULL AND OccurredAt IS NOT NULL;

UPDATE dbo.fn_outbox_message
SET ProcessedAtUtc = ProcessedAt
WHERE ProcessedAtUtc IS NULL AND ProcessedAt IS NOT NULL;

UPDATE dbo.fn_outbox_message
SET NextAttemptAtUtc = NextAttemptAt
WHERE NextAttemptAtUtc IS NULL AND NextAttemptAt IS NOT NULL;

UPDATE dbo.fn_outbox_message
SET LockedUntilUtc = LockedUntil
WHERE LockedUntilUtc IS NULL AND LockedUntil IS NOT NULL;
');
