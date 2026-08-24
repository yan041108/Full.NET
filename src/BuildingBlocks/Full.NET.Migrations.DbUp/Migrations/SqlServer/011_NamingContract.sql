-- 011 Contract：收紧 Outbox canonical 列并退役 legacy 表/列。
-- 该配对迁移共享维护窗口门禁，批准标识只用于审计，不得包含 Secret。
IF N'$PreV1NamingContractMaintenanceMode$' <> N'1'
    THROW 51000, 'Naming contract gate missing: maintenance mode', 1;
IF N'$PreV1NamingContractBackupVerified$' <> N'1'
    THROW 51000, 'Naming contract gate missing: verified backup', 1;
IF N'$PreV1NamingContractLegacyWritersStopped$' <> N'1'
    THROW 51000, 'Naming contract gate missing: legacy writers stopped', 1;
IF N'$PreV1NamingContractLegacyOutboxDrained$' <> N'1'
    THROW 51000, 'Naming contract gate missing: legacy outbox drained', 1;
IF N'$PreV1NamingContractDestructiveDdlApprovalId$' = N''
    THROW 51000, 'Naming contract gate missing: destructive DDL approval', 1;
IF NOT EXISTS(
    SELECT 1 FROM dbo.SchemaVersions
    WHERE ScriptName LIKE '%010_NamingExpand.sql')
    THROW 51000, 'Naming contract prerequisite missing: 010 expand journal', 1;
IF OBJECT_ID(N'dbo.fn_tenancy_tenant', N'U') IS NULL
    THROW 51000, 'Naming contract prerequisite missing: fn_tenancy_tenant', 1;

IF OBJECT_ID(N'dbo.fn_pre_v1_naming_contract_state', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_pre_v1_naming_contract_state
    (
        Id tinyint NOT NULL,
        SchemaMode varchar(16) NOT NULL,
        DestructiveDdlApprovalId varchar(64) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_pre_v1_naming_contract_state PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_pre_v1_naming_contract_state_Id CHECK (Id = 1),
        CONSTRAINT CK_fn_pre_v1_naming_contract_state_SchemaMode
            CHECK (SchemaMode IN ('Contracting', 'Contracted'))
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_pre_v1_naming_contract_state')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'1.0 前命名契约迁移状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_pre_v1_naming_contract_state';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_pre_v1_naming_contract_state')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_pre_v1_naming_contract_state'), N'DestructiveDdlApprovalId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'破坏性 DDL 审批标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_pre_v1_naming_contract_state', @level2type=N'COLUMN', @level2name=N'DestructiveDdlApprovalId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_pre_v1_naming_contract_state')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_pre_v1_naming_contract_state'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_pre_v1_naming_contract_state', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_pre_v1_naming_contract_state')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_pre_v1_naming_contract_state'), N'SchemaMode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Schema 模式', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_pre_v1_naming_contract_state', @level2type=N'COLUMN', @level2name=N'SchemaMode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_pre_v1_naming_contract_state')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_pre_v1_naming_contract_state'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_pre_v1_naming_contract_state', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
END;

IF EXISTS(
    SELECT 1 FROM dbo.fn_pre_v1_naming_contract_state
    WHERE Id = 1
      AND DestructiveDdlApprovalId <> '$PreV1NamingContractDestructiveDdlApprovalId$')
    THROW 51000, 'Naming contract approval mismatch', 1;

IF OBJECT_ID(N'dbo.fn_tenant_tenant', N'U') IS NOT NULL
BEGIN
    IF (SELECT COUNT(*) FROM dbo.fn_tenant_tenant)
        <> (SELECT COUNT(*) FROM dbo.fn_tenancy_tenant)
        THROW 51000, 'Naming contract tenant count mismatch', 1;

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
        THROW 51000, 'Naming contract tenant data mismatch', 1;
END;

IF COL_LENGTH(N'dbo.fn_outbox_message', N'MessageType') IS NULL
    THROW 51000, 'Naming contract prerequisite missing: MessageType column', 1;
IF COL_LENGTH(N'dbo.fn_outbox_message', N'OccurredAtUtc') IS NULL
    THROW 51000, 'Naming contract prerequisite missing: OccurredAtUtc column', 1;

IF COL_LENGTH(N'dbo.fn_outbox_message', N'Type') IS NOT NULL
BEGIN
    EXEC(N'
    IF EXISTS
    (
        SELECT 1
        FROM dbo.fn_outbox_message
        WHERE MessageType IS NOT NULL
          AND [Type] IS NOT NULL
          AND MessageType <> [Type]
    )
        THROW 50000, ''Naming contract outbox conflict: MessageType'', 1;

    IF EXISTS
    (
        SELECT 1 FROM dbo.fn_outbox_message
        WHERE OccurredAtUtc IS NOT NULL AND OccurredAt IS NOT NULL AND OccurredAtUtc <> OccurredAt
    )
        THROW 50000, ''Naming contract outbox conflict: OccurredAtUtc'', 1;

    IF EXISTS
    (
        SELECT 1 FROM dbo.fn_outbox_message
        WHERE ProcessedAtUtc IS NOT NULL AND ProcessedAt IS NOT NULL AND ProcessedAtUtc <> ProcessedAt
    )
        THROW 50000, ''Naming contract outbox conflict: ProcessedAtUtc'', 1;

    IF EXISTS
    (
        SELECT 1 FROM dbo.fn_outbox_message
        WHERE NextAttemptAtUtc IS NOT NULL AND NextAttemptAt IS NOT NULL AND NextAttemptAtUtc <> NextAttemptAt
    )
        THROW 50000, ''Naming contract outbox conflict: NextAttemptAtUtc'', 1;

    IF EXISTS
    (
        SELECT 1 FROM dbo.fn_outbox_message
        WHERE LockedUntilUtc IS NOT NULL AND LockedUntil IS NOT NULL AND LockedUntilUtc <> LockedUntil
    )
        THROW 50000, ''Naming contract outbox conflict: LockedUntilUtc'', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.fn_outbox_message
        WHERE COALESCE(ProcessedAtUtc, ProcessedAt) IS NULL
          AND (MessageType IS NULL OR OccurredAtUtc IS NULL)
    )
        THROW 50000, ''Naming contract legacy pending outbox'', 1;

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
END
ELSE IF EXISTS
(
    SELECT 1
    FROM dbo.fn_outbox_message
    WHERE ProcessedAtUtc IS NULL
      AND (MessageType IS NULL OR OccurredAtUtc IS NULL)
)
    THROW 51000, 'Naming contract legacy pending outbox', 1;

IF EXISTS(SELECT 1 FROM dbo.fn_outbox_message WHERE MessageType IS NULL)
    THROW 51000, 'Naming contract outbox null: MessageType', 1;
IF EXISTS(SELECT 1 FROM dbo.fn_outbox_message WHERE OccurredAtUtc IS NULL)
    THROW 51000, 'Naming contract outbox null: OccurredAtUtc', 1;

MERGE dbo.fn_pre_v1_naming_contract_state AS target
USING
(
    SELECT CAST(1 AS tinyint) AS Id,
           CAST('Contracting' AS varchar(16)) AS SchemaMode,
           CAST('$PreV1NamingContractDestructiveDdlApprovalId$' AS varchar(64)) AS DestructiveDdlApprovalId,
           SYSDATETIMEOFFSET() AS UpdatedAtUtc
) AS source
ON target.Id = source.Id
WHEN MATCHED THEN
    UPDATE SET SchemaMode = source.SchemaMode,
               UpdatedAtUtc = source.UpdatedAtUtc
WHEN NOT MATCHED THEN
    INSERT (Id, SchemaMode, DestructiveDdlApprovalId, UpdatedAtUtc)
    VALUES (source.Id, source.SchemaMode, source.DestructiveDdlApprovalId, source.UpdatedAtUtc);

IF EXISTS(SELECT 1 FROM sys.indexes
          WHERE object_id = OBJECT_ID(N'dbo.fn_outbox_message')
            AND name = N'IX_fn_outbox_message_OccurredAt_Id')
    DROP INDEX IX_fn_outbox_message_OccurredAt_Id ON dbo.fn_outbox_message;
IF EXISTS(SELECT 1 FROM sys.indexes
          WHERE object_id = OBJECT_ID(N'dbo.fn_outbox_message')
            AND name = N'IX_fn_outbox_message_Pending')
    DROP INDEX IX_fn_outbox_message_Pending ON dbo.fn_outbox_message;

IF EXISTS(
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.fn_outbox_message')
      AND name = N'MessageType'
      AND is_nullable = 1)
    ALTER TABLE dbo.fn_outbox_message ALTER COLUMN MessageType nvarchar(256) NOT NULL;
IF EXISTS(
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.fn_outbox_message')
      AND name = N'OccurredAtUtc'
      AND is_nullable = 1)
    ALTER TABLE dbo.fn_outbox_message ALTER COLUMN OccurredAtUtc datetimeoffset(7) NOT NULL;

IF NOT EXISTS(SELECT 1 FROM sys.indexes
              WHERE object_id = OBJECT_ID(N'dbo.fn_outbox_message')
                AND name = N'IX_fn_outbox_message_OccurredAtUtc_Id')
    CREATE CLUSTERED INDEX IX_fn_outbox_message_OccurredAtUtc_Id
        ON dbo.fn_outbox_message(OccurredAtUtc, Id);
IF NOT EXISTS(SELECT 1 FROM sys.indexes
              WHERE object_id = OBJECT_ID(N'dbo.fn_outbox_message')
                AND name = N'IX_fn_outbox_message_Pending')
    CREATE INDEX IX_fn_outbox_message_Pending
        ON dbo.fn_outbox_message(ProcessedAtUtc, NextAttemptAtUtc, LockedUntilUtc, OccurredAtUtc);

IF COL_LENGTH(N'dbo.fn_outbox_message', N'Type') IS NOT NULL
    ALTER TABLE dbo.fn_outbox_message DROP COLUMN Type;
IF COL_LENGTH(N'dbo.fn_outbox_message', N'OccurredAt') IS NOT NULL
    ALTER TABLE dbo.fn_outbox_message DROP COLUMN OccurredAt;
IF COL_LENGTH(N'dbo.fn_outbox_message', N'ProcessedAt') IS NOT NULL
    ALTER TABLE dbo.fn_outbox_message DROP COLUMN ProcessedAt;
IF COL_LENGTH(N'dbo.fn_outbox_message', N'NextAttemptAt') IS NOT NULL
    ALTER TABLE dbo.fn_outbox_message DROP COLUMN NextAttemptAt;
IF COL_LENGTH(N'dbo.fn_outbox_message', N'LockedUntil') IS NOT NULL
    ALTER TABLE dbo.fn_outbox_message DROP COLUMN LockedUntil;

IF OBJECT_ID(N'dbo.fn_tenant_tenant', N'U') IS NOT NULL
    DROP TABLE dbo.fn_tenant_tenant;

UPDATE dbo.fn_pre_v1_naming_contract_state
SET SchemaMode = 'Contracted',
    UpdatedAtUtc = SYSDATETIMEOFFSET()
WHERE Id = 1;
