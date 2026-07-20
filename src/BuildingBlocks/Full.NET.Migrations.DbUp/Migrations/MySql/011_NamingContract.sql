-- 011 Contract：收紧 Outbox canonical 列并退役 legacy 表/列。
DROP PROCEDURE IF EXISTS fn_pre_v1_naming_contract_gate;
DELIMITER $$
CREATE PROCEDURE fn_pre_v1_naming_contract_gate()
BEGIN
    IF '$PreV1NamingContractMaintenanceMode$' <> '1' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Naming contract gate missing: maintenance mode';
    END IF;
    IF '$PreV1NamingContractBackupVerified$' <> '1' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Naming contract gate missing: verified backup';
    END IF;
    IF '$PreV1NamingContractLegacyWritersStopped$' <> '1' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Naming contract gate missing: legacy writers stopped';
    END IF;
    IF '$PreV1NamingContractLegacyOutboxDrained$' <> '1' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Naming contract gate missing: legacy outbox drained';
    END IF;
    IF '$PreV1NamingContractDestructiveDdlApprovalId$' = '' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Naming contract gate missing: destructive DDL approval';
    END IF;
    IF NOT EXISTS(
        SELECT 1
        FROM schemaversions
        WHERE ScriptName LIKE '%010_NamingExpand.sql') THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Naming contract prerequisite missing: 010 expand journal';
    END IF;
END$$
DELIMITER ;
CALL fn_pre_v1_naming_contract_gate();
DROP PROCEDURE fn_pre_v1_naming_contract_gate;

-- PREPARE 协议禁止 SIGNAL；条件失败统一经存储过程抛出。
DROP PROCEDURE IF EXISTS fn_pre_v1_naming_contract_fail_if;
DELIMITER $$
CREATE PROCEDURE fn_pre_v1_naming_contract_fail_if(
    IN should_fail int,
    IN message_text varchar(128))
BEGIN
    IF should_fail > 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = message_text;
    END IF;
END$$
DELIMITER ;

CREATE TABLE IF NOT EXISTS fn_pre_v1_naming_contract_state
(
    Id tinyint NOT NULL,
    SchemaMode varchar(16) NOT NULL,
    DestructiveDdlApprovalId varchar(64) NOT NULL,
    UpdatedAtUtc datetime(6) NOT NULL,
    CONSTRAINT PK_fn_pre_v1_naming_contract_state PRIMARY KEY (Id),
    CONSTRAINT CK_fn_pre_v1_naming_contract_state_Id CHECK (Id = 1),
    CONSTRAINT CK_fn_pre_v1_naming_contract_state_SchemaMode
        CHECK (SchemaMode IN ('Contracting', 'Contracted'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

SET @naming_tenancy_table := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_tenancy_tenant'
);
CALL fn_pre_v1_naming_contract_fail_if(
    IF(@naming_tenancy_table = 0, 1, 0),
    'Naming contract prerequisite missing: fn_tenancy_tenant');

SET @naming_approval_mismatch := (
    SELECT COUNT(*)
    FROM fn_pre_v1_naming_contract_state
    WHERE Id = 1
      AND DestructiveDdlApprovalId <> '$PreV1NamingContractDestructiveDdlApprovalId$'
);
CALL fn_pre_v1_naming_contract_fail_if(
    @naming_approval_mismatch,
    'Naming contract approval mismatch');

INSERT INTO fn_pre_v1_naming_contract_state
    (Id, SchemaMode, DestructiveDdlApprovalId, UpdatedAtUtc)
SELECT 1, 'Contracting', '$PreV1NamingContractDestructiveDdlApprovalId$', UTC_TIMESTAMP(6)
WHERE NOT EXISTS(SELECT 1 FROM fn_pre_v1_naming_contract_state WHERE Id = 1);

UPDATE fn_pre_v1_naming_contract_state
SET SchemaMode = 'Contracting',
    UpdatedAtUtc = UTC_TIMESTAMP(6)
WHERE Id = 1;

SET @legacy_tenant_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_tenant_tenant'
);
SET @tenant_count_mismatch_calc_sql := IF(
    @legacy_tenant_exists = 0,
    'SELECT 0 INTO @tenant_count_mismatch',
    'SELECT IF((SELECT COUNT(*) FROM fn_tenant_tenant) <> (SELECT COUNT(*) FROM fn_tenancy_tenant), 1, 0) INTO @tenant_count_mismatch');
PREPARE tenant_count_mismatch_calc_stmt FROM @tenant_count_mismatch_calc_sql;
EXECUTE tenant_count_mismatch_calc_stmt;
DEALLOCATE PREPARE tenant_count_mismatch_calc_stmt;
CALL fn_pre_v1_naming_contract_fail_if(
    @tenant_count_mismatch,
    'Naming contract tenant count mismatch');

SET @tenant_data_mismatch_calc_sql := IF(
    @legacy_tenant_exists = 0,
    'SELECT 0 INTO @tenant_data_mismatch',
    CONCAT(
        'SELECT COUNT(*) INTO @tenant_data_mismatch FROM fn_tenant_tenant AS legacy ',
        'INNER JOIN fn_tenancy_tenant AS canonical ON canonical.Id = legacy.Id ',
        'WHERE canonical.Identifier <> legacy.Identifier ',
        'OR canonical.Name <> legacy.Name ',
        'OR canonical.Domain <> legacy.Domain ',
        'OR canonical.IsActive <> legacy.IsActive ',
        'OR canonical.CreatedAtUtc <> legacy.CreatedAt ',
        'OR (canonical.UpdatedAtUtc IS NULL) <> (legacy.UpdatedAt IS NULL) ',
        'OR (canonical.UpdatedAtUtc IS NOT NULL AND legacy.UpdatedAt IS NOT NULL ',
        'AND canonical.UpdatedAtUtc <> legacy.UpdatedAt) ',
        'OR canonical.DefaultLocale <> legacy.DefaultLocale ',
        'OR canonical.Version <> legacy.Version'));
PREPARE tenant_data_mismatch_calc_stmt FROM @tenant_data_mismatch_calc_sql;
EXECUTE tenant_data_mismatch_calc_stmt;
DEALLOCATE PREPARE tenant_data_mismatch_calc_stmt;
CALL fn_pre_v1_naming_contract_fail_if(
    @tenant_data_mismatch,
    'Naming contract tenant data mismatch');

SET @message_type_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_outbox_message'
      AND COLUMN_NAME = 'MessageType'
);
CALL fn_pre_v1_naming_contract_fail_if(
    IF(@message_type_exists = 0, 1, 0),
    'Naming contract prerequisite missing: MessageType column');

SET @occurred_at_utc_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_outbox_message'
      AND COLUMN_NAME = 'OccurredAtUtc'
);
CALL fn_pre_v1_naming_contract_fail_if(
    IF(@occurred_at_utc_exists = 0, 1, 0),
    'Naming contract prerequisite missing: OccurredAtUtc column');

SET @legacy_type_column := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_outbox_message'
      AND COLUMN_NAME = 'Type'
);

SET @outbox_type_conflict_calc_sql := IF(
    @legacy_type_column = 0,
    'SELECT 0 INTO @outbox_type_conflict',
    'SELECT COUNT(*) INTO @outbox_type_conflict FROM fn_outbox_message WHERE MessageType IS NOT NULL AND Type IS NOT NULL AND MessageType <> Type');
PREPARE outbox_type_conflict_calc_stmt FROM @outbox_type_conflict_calc_sql;
EXECUTE outbox_type_conflict_calc_stmt;
DEALLOCATE PREPARE outbox_type_conflict_calc_stmt;
CALL fn_pre_v1_naming_contract_fail_if(
    @outbox_type_conflict,
    'Naming contract outbox conflict: MessageType');

SET @legacy_pending_outbox_calc_sql := IF(
    @legacy_type_column = 0,
    'SELECT COUNT(*) INTO @legacy_pending_outbox FROM fn_outbox_message WHERE ProcessedAtUtc IS NULL AND (MessageType IS NULL OR OccurredAtUtc IS NULL)',
    'SELECT COUNT(*) INTO @legacy_pending_outbox FROM fn_outbox_message WHERE COALESCE(ProcessedAtUtc, ProcessedAt) IS NULL AND (MessageType IS NULL OR OccurredAtUtc IS NULL)');
PREPARE legacy_pending_outbox_calc_stmt FROM @legacy_pending_outbox_calc_sql;
EXECUTE legacy_pending_outbox_calc_stmt;
DEALLOCATE PREPARE legacy_pending_outbox_calc_stmt;
CALL fn_pre_v1_naming_contract_fail_if(
    @legacy_pending_outbox,
    'Naming contract legacy pending outbox');

SET @backfill_message_type_sql := IF(
    @legacy_type_column = 0,
    'SELECT 1',
    'UPDATE fn_outbox_message SET MessageType = Type WHERE MessageType IS NULL AND Type IS NOT NULL');
PREPARE backfill_message_type_stmt FROM @backfill_message_type_sql;
EXECUTE backfill_message_type_stmt;
DEALLOCATE PREPARE backfill_message_type_stmt;

SET @backfill_occurred_sql := IF(
    @legacy_type_column = 0,
    'SELECT 1',
    'UPDATE fn_outbox_message SET OccurredAtUtc = OccurredAt WHERE OccurredAtUtc IS NULL AND OccurredAt IS NOT NULL');
PREPARE backfill_occurred_stmt FROM @backfill_occurred_sql;
EXECUTE backfill_occurred_stmt;
DEALLOCATE PREPARE backfill_occurred_stmt;

SET @backfill_processed_sql := IF(
    @legacy_type_column = 0,
    'SELECT 1',
    'UPDATE fn_outbox_message SET ProcessedAtUtc = ProcessedAt WHERE ProcessedAtUtc IS NULL AND ProcessedAt IS NOT NULL');
PREPARE backfill_processed_stmt FROM @backfill_processed_sql;
EXECUTE backfill_processed_stmt;
DEALLOCATE PREPARE backfill_processed_stmt;

SET @backfill_next_attempt_sql := IF(
    @legacy_type_column = 0,
    'SELECT 1',
    'UPDATE fn_outbox_message SET NextAttemptAtUtc = NextAttemptAt WHERE NextAttemptAtUtc IS NULL AND NextAttemptAt IS NOT NULL');
PREPARE backfill_next_attempt_stmt FROM @backfill_next_attempt_sql;
EXECUTE backfill_next_attempt_stmt;
DEALLOCATE PREPARE backfill_next_attempt_stmt;

SET @backfill_locked_until_sql := IF(
    @legacy_type_column = 0,
    'SELECT 1',
    'UPDATE fn_outbox_message SET LockedUntilUtc = LockedUntil WHERE LockedUntilUtc IS NULL AND LockedUntil IS NOT NULL');
PREPARE backfill_locked_until_stmt FROM @backfill_locked_until_sql;
EXECUTE backfill_locked_until_stmt;
DEALLOCATE PREPARE backfill_locked_until_stmt;

SET @null_message_type := (
    SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType IS NULL
);
CALL fn_pre_v1_naming_contract_fail_if(
    @null_message_type,
    'Naming contract outbox null: MessageType');

SET @null_occurred_at_utc := (
    SELECT COUNT(*) FROM fn_outbox_message WHERE OccurredAtUtc IS NULL
);
CALL fn_pre_v1_naming_contract_fail_if(
    @null_occurred_at_utc,
    'Naming contract outbox null: OccurredAtUtc');

SET @drop_pending_index := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_outbox_message'
      AND INDEX_NAME = 'IX_fn_outbox_message_Pending'
);
SET @drop_pending_index_sql := IF(
    @drop_pending_index > 0,
    'ALTER TABLE fn_outbox_message DROP INDEX IX_fn_outbox_message_Pending',
    'SELECT 1');
PREPARE drop_pending_index_stmt FROM @drop_pending_index_sql;
EXECUTE drop_pending_index_stmt;
DEALLOCATE PREPARE drop_pending_index_stmt;

SET @message_type_nullable := (
    SELECT IS_NULLABLE = 'YES'
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_outbox_message'
      AND COLUMN_NAME = 'MessageType'
);
SET @message_type_nullable_sql := IF(
    @message_type_nullable,
    'ALTER TABLE fn_outbox_message MODIFY COLUMN MessageType varchar(256) NOT NULL',
    'SELECT 1');
PREPARE message_type_nullable_stmt FROM @message_type_nullable_sql;
EXECUTE message_type_nullable_stmt;
DEALLOCATE PREPARE message_type_nullable_stmt;

SET @occurred_at_utc_nullable := (
    SELECT IS_NULLABLE = 'YES'
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_outbox_message'
      AND COLUMN_NAME = 'OccurredAtUtc'
);
SET @occurred_at_utc_nullable_sql := IF(
    @occurred_at_utc_nullable,
    'ALTER TABLE fn_outbox_message MODIFY COLUMN OccurredAtUtc datetime(6) NOT NULL',
    'SELECT 1');
PREPARE occurred_at_utc_nullable_stmt FROM @occurred_at_utc_nullable_sql;
EXECUTE occurred_at_utc_nullable_stmt;
DEALLOCATE PREPARE occurred_at_utc_nullable_stmt;

SET @create_pending_index := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_outbox_message'
      AND INDEX_NAME = 'IX_fn_outbox_message_Pending'
);
SET @create_pending_index_sql := IF(
    @create_pending_index > 0,
    'SELECT 1',
    'CREATE INDEX IX_fn_outbox_message_Pending ON fn_outbox_message (ProcessedAtUtc, NextAttemptAtUtc, LockedUntilUtc, OccurredAtUtc)');
PREPARE create_pending_index_stmt FROM @create_pending_index_sql;
EXECUTE create_pending_index_stmt;
DEALLOCATE PREPARE create_pending_index_stmt;

SET @drop_type_column := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_outbox_message'
      AND COLUMN_NAME = 'Type'
);
SET @drop_type_column_sql := IF(
    @drop_type_column > 0,
    'ALTER TABLE fn_outbox_message DROP COLUMN Type',
    'SELECT 1');
PREPARE drop_type_column_stmt FROM @drop_type_column_sql;
EXECUTE drop_type_column_stmt;
DEALLOCATE PREPARE drop_type_column_stmt;

SET @drop_occurred_at_column := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_outbox_message'
      AND COLUMN_NAME = 'OccurredAt'
);
SET @drop_occurred_at_column_sql := IF(
    @drop_occurred_at_column > 0,
    'ALTER TABLE fn_outbox_message DROP COLUMN OccurredAt',
    'SELECT 1');
PREPARE drop_occurred_at_column_stmt FROM @drop_occurred_at_column_sql;
EXECUTE drop_occurred_at_column_stmt;
DEALLOCATE PREPARE drop_occurred_at_column_stmt;

SET @drop_processed_at_column := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_outbox_message'
      AND COLUMN_NAME = 'ProcessedAt'
);
SET @drop_processed_at_column_sql := IF(
    @drop_processed_at_column > 0,
    'ALTER TABLE fn_outbox_message DROP COLUMN ProcessedAt',
    'SELECT 1');
PREPARE drop_processed_at_column_stmt FROM @drop_processed_at_column_sql;
EXECUTE drop_processed_at_column_stmt;
DEALLOCATE PREPARE drop_processed_at_column_stmt;

SET @drop_next_attempt_at_column := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_outbox_message'
      AND COLUMN_NAME = 'NextAttemptAt'
);
SET @drop_next_attempt_at_column_sql := IF(
    @drop_next_attempt_at_column > 0,
    'ALTER TABLE fn_outbox_message DROP COLUMN NextAttemptAt',
    'SELECT 1');
PREPARE drop_next_attempt_at_column_stmt FROM @drop_next_attempt_at_column_sql;
EXECUTE drop_next_attempt_at_column_stmt;
DEALLOCATE PREPARE drop_next_attempt_at_column_stmt;

SET @drop_locked_until_column := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_outbox_message'
      AND COLUMN_NAME = 'LockedUntil'
);
SET @drop_locked_until_column_sql := IF(
    @drop_locked_until_column > 0,
    'ALTER TABLE fn_outbox_message DROP COLUMN LockedUntil',
    'SELECT 1');
PREPARE drop_locked_until_column_stmt FROM @drop_locked_until_column_sql;
EXECUTE drop_locked_until_column_stmt;
DEALLOCATE PREPARE drop_locked_until_column_stmt;

SET @drop_legacy_tenant := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_tenant_tenant'
);
SET @drop_legacy_tenant_sql := IF(
    @drop_legacy_tenant > 0,
    'DROP TABLE fn_tenant_tenant',
    'SELECT 1');
PREPARE drop_legacy_tenant_stmt FROM @drop_legacy_tenant_sql;
EXECUTE drop_legacy_tenant_stmt;
DEALLOCATE PREPARE drop_legacy_tenant_stmt;

UPDATE fn_pre_v1_naming_contract_state
SET SchemaMode = 'Contracted',
    UpdatedAtUtc = UTC_TIMESTAMP(6)
WHERE Id = 1;

DROP PROCEDURE IF EXISTS fn_pre_v1_naming_contract_fail_if;
