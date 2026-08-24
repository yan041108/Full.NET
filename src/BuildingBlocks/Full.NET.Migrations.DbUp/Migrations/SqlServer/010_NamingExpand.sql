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
        DefaultLocale varchar(35) NOT NULL
            CONSTRAINT DF_fn_tenancy_tenant_DefaultLocale DEFAULT ('zh-CN'),
        Version int NOT NULL CONSTRAINT DF_fn_tenancy_tenant_Version DEFAULT (1),
        CONSTRAINT PK_fn_tenancy_tenant PRIMARY KEY CLUSTERED (Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户租户表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant'), N'DefaultLocale', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'默认语言区域', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant', @level2type=N'COLUMN', @level2name=N'DefaultLocale';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant'), N'Domain', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'域名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant', @level2type=N'COLUMN', @level2name=N'Domain';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant'), N'Identifier', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一标识符', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant', @level2type=N'COLUMN', @level2name=N'Identifier';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant'), N'IsActive', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant', @level2type=N'COLUMN', @level2name=N'IsActive';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant'), N'Name', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant', @level2type=N'COLUMN', @level2name=N'Name';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_tenancy_tenant')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_tenant'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_tenancy_tenant', @level2type=N'COLUMN', @level2name=N'Version';
    CREATE UNIQUE INDEX UX_fn_tenancy_tenant_Identifier ON dbo.fn_tenancy_tenant(Identifier);
    CREATE UNIQUE INDEX UX_fn_tenancy_tenant_Domain ON dbo.fn_tenancy_tenant(Domain);
END;

IF OBJECT_ID(N'dbo.fn_tenancy_tenant', N'U') IS NOT NULL
   AND NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_tenancy_tenant')
      AND name = N'DF_fn_tenancy_tenant_DefaultLocale'
)
    ALTER TABLE dbo.fn_tenancy_tenant
        ADD CONSTRAINT DF_fn_tenancy_tenant_DefaultLocale
        DEFAULT ('zh-CN') FOR DefaultLocale;

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
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_outbox_message')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_outbox_message'), N'MessageType', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消息类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_outbox_message', @level2type=N'COLUMN', @level2name=N'MessageType';
IF COL_LENGTH(N'dbo.fn_outbox_message', N'OccurredAtUtc') IS NULL
    ALTER TABLE dbo.fn_outbox_message ADD OccurredAtUtc datetimeoffset(7) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_outbox_message')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_outbox_message'), N'OccurredAtUtc', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发生时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_outbox_message', @level2type=N'COLUMN', @level2name=N'OccurredAtUtc';
IF COL_LENGTH(N'dbo.fn_outbox_message', N'ProcessedAtUtc') IS NULL
    ALTER TABLE dbo.fn_outbox_message ADD ProcessedAtUtc datetimeoffset(7) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_outbox_message')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_outbox_message'), N'ProcessedAtUtc', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'处理完成时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_outbox_message', @level2type=N'COLUMN', @level2name=N'ProcessedAtUtc';
IF COL_LENGTH(N'dbo.fn_outbox_message', N'NextAttemptAtUtc') IS NULL
    ALTER TABLE dbo.fn_outbox_message ADD NextAttemptAtUtc datetimeoffset(7) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_outbox_message')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_outbox_message'), N'NextAttemptAtUtc', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'下次重试时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_outbox_message', @level2type=N'COLUMN', @level2name=N'NextAttemptAtUtc';
IF COL_LENGTH(N'dbo.fn_outbox_message', N'LockedUntilUtc') IS NULL
    ALTER TABLE dbo.fn_outbox_message ADD LockedUntilUtc datetimeoffset(7) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_outbox_message')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_outbox_message'), N'LockedUntilUtc', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'锁定截止时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_outbox_message', @level2type=N'COLUMN', @level2name=N'LockedUntilUtc';

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

IF EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.fn_outbox_message')
      AND name = N'Type'
      AND is_nullable = 0
)
    ALTER TABLE dbo.fn_outbox_message ALTER COLUMN Type nvarchar(256) NULL;

IF EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.fn_outbox_message')
      AND name = N'OccurredAt'
      AND is_nullable = 0
)
    ALTER TABLE dbo.fn_outbox_message ALTER COLUMN OccurredAt datetimeoffset(7) NULL;
