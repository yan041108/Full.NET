-- 010 Expand：新增规范 Tenancy 表与 Outbox 镜像列；legacy 对象保持可用。
CREATE TABLE IF NOT EXISTS fn_tenancy_tenant
(
    Id BINARY(16) NOT NULL,
    Identifier varchar(64) NOT NULL,
    Name varchar(128) NOT NULL,
    Domain varchar(255) NOT NULL,
    IsActive boolean NOT NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    DefaultLocale varchar(35) NOT NULL DEFAULT 'zh-CN',
    Version int NOT NULL DEFAULT 1,
    CONSTRAINT PK_fn_tenancy_tenant PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_tenancy_tenant_Identifier (Identifier),
    UNIQUE KEY UX_fn_tenancy_tenant_Domain (Domain)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- CREATE IF NOT EXISTS 不会改已有表；补齐 DefaultLocale 默认值以匹配 004 语义。
SET @tenancy_default_locale := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_tenancy_tenant'
      AND COLUMN_NAME = 'DefaultLocale'
      AND COLUMN_DEFAULT IS NOT NULL
);
SET @tenancy_default_locale_sql := IF(
    @tenancy_default_locale = 0,
    'ALTER TABLE fn_tenancy_tenant MODIFY COLUMN DefaultLocale varchar(35) NOT NULL DEFAULT ''zh-CN''',
    'SELECT 1');
PREPARE tenancy_default_locale_stmt FROM @tenancy_default_locale_sql;
EXECUTE tenancy_default_locale_stmt;
DEALLOCATE PREPARE tenancy_default_locale_stmt;

SET @naming_conflict := (
    SELECT COUNT(*)
    FROM fn_tenant_tenant AS legacy
    INNER JOIN fn_tenancy_tenant AS canonical ON canonical.Id = legacy.Id
    WHERE canonical.Identifier <> legacy.Identifier
       OR canonical.Name <> legacy.Name
       OR canonical.Domain <> legacy.Domain
       OR canonical.IsActive <> legacy.IsActive
       OR canonical.CreatedAtUtc <> legacy.CreatedAt
       OR (canonical.UpdatedAtUtc IS NULL) <> (legacy.UpdatedAt IS NULL)
       OR (canonical.UpdatedAtUtc IS NOT NULL AND legacy.UpdatedAt IS NOT NULL
           AND canonical.UpdatedAtUtc <> legacy.UpdatedAt)
       OR canonical.DefaultLocale <> legacy.DefaultLocale
       OR canonical.Version <> legacy.Version
);
SET @naming_conflict_sql := IF(
    @naming_conflict > 0,
    'SIGNAL SQLSTATE ''45000'' SET MESSAGE_TEXT = ''Tenant naming conflict: fn_tenancy_tenant count=1''',
    'SELECT 1');
PREPARE naming_conflict_stmt FROM @naming_conflict_sql;
EXECUTE naming_conflict_stmt;
DEALLOCATE PREPARE naming_conflict_stmt;

INSERT INTO fn_tenancy_tenant
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
FROM fn_tenant_tenant AS legacy
WHERE NOT EXISTS
(
    SELECT 1 FROM fn_tenancy_tenant AS canonical WHERE canonical.Id = legacy.Id
);

SET @add_message_type := (
    SELECT COUNT(*) = 0
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_outbox_message'
      AND COLUMN_NAME = 'MessageType'
);
SET @add_message_type_sql := IF(
    @add_message_type,
    'ALTER TABLE fn_outbox_message ADD COLUMN MessageType varchar(256) NULL',
    'SELECT 1');
PREPARE add_message_type_stmt FROM @add_message_type_sql;
EXECUTE add_message_type_stmt;
DEALLOCATE PREPARE add_message_type_stmt;

SET @add_occurred_at_utc := (
    SELECT COUNT(*) = 0
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_outbox_message'
      AND COLUMN_NAME = 'OccurredAtUtc'
);
SET @add_occurred_at_utc_sql := IF(
    @add_occurred_at_utc,
    'ALTER TABLE fn_outbox_message ADD COLUMN OccurredAtUtc datetime(6) NULL',
    'SELECT 1');
PREPARE add_occurred_at_utc_stmt FROM @add_occurred_at_utc_sql;
EXECUTE add_occurred_at_utc_stmt;
DEALLOCATE PREPARE add_occurred_at_utc_stmt;

SET @add_processed_at_utc := (
    SELECT COUNT(*) = 0
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_outbox_message'
      AND COLUMN_NAME = 'ProcessedAtUtc'
);
SET @add_processed_at_utc_sql := IF(
    @add_processed_at_utc,
    'ALTER TABLE fn_outbox_message ADD COLUMN ProcessedAtUtc datetime(6) NULL',
    'SELECT 1');
PREPARE add_processed_at_utc_stmt FROM @add_processed_at_utc_sql;
EXECUTE add_processed_at_utc_stmt;
DEALLOCATE PREPARE add_processed_at_utc_stmt;

SET @add_next_attempt_at_utc := (
    SELECT COUNT(*) = 0
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_outbox_message'
      AND COLUMN_NAME = 'NextAttemptAtUtc'
);
SET @add_next_attempt_at_utc_sql := IF(
    @add_next_attempt_at_utc,
    'ALTER TABLE fn_outbox_message ADD COLUMN NextAttemptAtUtc datetime(6) NULL',
    'SELECT 1');
PREPARE add_next_attempt_at_utc_stmt FROM @add_next_attempt_at_utc_sql;
EXECUTE add_next_attempt_at_utc_stmt;
DEALLOCATE PREPARE add_next_attempt_at_utc_stmt;

SET @add_locked_until_utc := (
    SELECT COUNT(*) = 0
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_outbox_message'
      AND COLUMN_NAME = 'LockedUntilUtc'
);
SET @add_locked_until_utc_sql := IF(
    @add_locked_until_utc,
    'ALTER TABLE fn_outbox_message ADD COLUMN LockedUntilUtc datetime(6) NULL',
    'SELECT 1');
PREPARE add_locked_until_utc_stmt FROM @add_locked_until_utc_sql;
EXECUTE add_locked_until_utc_stmt;
DEALLOCATE PREPARE add_locked_until_utc_stmt;

SET @outbox_type_conflict := (
    SELECT COUNT(*)
    FROM fn_outbox_message
    WHERE MessageType IS NOT NULL
      AND Type IS NOT NULL
      AND MessageType <> Type
);
SET @outbox_type_conflict_sql := IF(
    @outbox_type_conflict > 0,
    'SIGNAL SQLSTATE ''45000'' SET MESSAGE_TEXT = ''Outbox naming conflict: MessageType count=1''',
    'SELECT 1');
PREPARE outbox_type_conflict_stmt FROM @outbox_type_conflict_sql;
EXECUTE outbox_type_conflict_stmt;
DEALLOCATE PREPARE outbox_type_conflict_stmt;

UPDATE fn_outbox_message
SET MessageType = Type
WHERE MessageType IS NULL AND Type IS NOT NULL;

UPDATE fn_outbox_message
SET OccurredAtUtc = OccurredAt
WHERE OccurredAtUtc IS NULL AND OccurredAt IS NOT NULL;

UPDATE fn_outbox_message
SET ProcessedAtUtc = ProcessedAt
WHERE ProcessedAtUtc IS NULL AND ProcessedAt IS NOT NULL;

UPDATE fn_outbox_message
SET NextAttemptAtUtc = NextAttemptAt
WHERE NextAttemptAtUtc IS NULL AND NextAttemptAt IS NOT NULL;

UPDATE fn_outbox_message
SET LockedUntilUtc = LockedUntil
WHERE LockedUntilUtc IS NULL AND LockedUntil IS NOT NULL;

SET @legacy_type_nullable := (
    SELECT IS_NULLABLE = 'YES'
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_outbox_message'
      AND COLUMN_NAME = 'Type'
);
SET @legacy_type_nullable_sql := IF(
    @legacy_type_nullable,
    'SELECT 1',
    'ALTER TABLE fn_outbox_message MODIFY COLUMN Type varchar(256) NULL');
PREPARE legacy_type_nullable_stmt FROM @legacy_type_nullable_sql;
EXECUTE legacy_type_nullable_stmt;
DEALLOCATE PREPARE legacy_type_nullable_stmt;

SET @legacy_occurred_nullable := (
    SELECT IS_NULLABLE = 'YES'
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_outbox_message'
      AND COLUMN_NAME = 'OccurredAt'
);
SET @legacy_occurred_nullable_sql := IF(
    @legacy_occurred_nullable,
    'SELECT 1',
    'ALTER TABLE fn_outbox_message MODIFY COLUMN OccurredAt datetime(6) NULL');
PREPARE legacy_occurred_nullable_stmt FROM @legacy_occurred_nullable_sql;
EXECUTE legacy_occurred_nullable_stmt;
DEALLOCATE PREPARE legacy_occurred_nullable_stmt;
