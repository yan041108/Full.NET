-- 095：在不修改已记账 094 的前提下，收敛事件流所有权约束并补种试点基线。

DROP PROCEDURE IF EXISTS fn_messaging_stream_ownership_constraints;
DELIMITER $$
CREATE PROCEDURE fn_messaging_stream_ownership_constraints()
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_messaging_stream_ownership'
          AND COLUMN_NAME = 'RollbackState'
    ) THEN
        ALTER TABLE fn_messaging_stream_ownership
            ADD COLUMN RollbackState tinyint NOT NULL DEFAULT 0;
    END IF;
    IF NOT EXISTS
    (
        SELECT 1
        FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_messaging_stream_ownership'
          AND COLUMN_NAME = 'RollbackGeneration'
    ) THEN
        ALTER TABLE fn_messaging_stream_ownership
            ADD COLUMN RollbackGeneration binary(16) NULL;
    END IF;
    IF NOT EXISTS
    (
        SELECT 1
        FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_messaging_stream_ownership'
          AND COLUMN_NAME = 'RollbackPreparedAtUtc'
    ) THEN
        ALTER TABLE fn_messaging_stream_ownership
            ADD COLUMN RollbackPreparedAtUtc datetime(6) NULL;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1
        FROM information_schema.TABLE_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_messaging_stream_ownership'
          AND CONSTRAINT_NAME = 'CK_fn_messaging_stream_ownership_SchemaVersion'
          AND CONSTRAINT_TYPE = 'CHECK'
    ) THEN
        ALTER TABLE fn_messaging_stream_ownership
            ADD CONSTRAINT CK_fn_messaging_stream_ownership_SchemaVersion
                CHECK (SchemaVersion BETWEEN 1 AND 65535);
    END IF;

    IF NOT EXISTS
    (
        SELECT 1
        FROM information_schema.TABLE_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_messaging_stream_ownership'
          AND CONSTRAINT_NAME = 'CK_fn_messaging_stream_ownership_CurrentOwner'
          AND CONSTRAINT_TYPE = 'CHECK'
    ) THEN
        ALTER TABLE fn_messaging_stream_ownership
            ADD CONSTRAINT CK_fn_messaging_stream_ownership_CurrentOwner
                CHECK (CurrentOwner BETWEEN 0 AND 2);
    END IF;

    IF NOT EXISTS
    (
        SELECT 1
        FROM information_schema.TABLE_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_messaging_stream_ownership'
          AND CONSTRAINT_NAME = 'CK_fn_messaging_stream_ownership_PreviousOwner'
          AND CONSTRAINT_TYPE = 'CHECK'
    ) THEN
        ALTER TABLE fn_messaging_stream_ownership
            ADD CONSTRAINT CK_fn_messaging_stream_ownership_PreviousOwner
                CHECK (PreviousOwner BETWEEN 0 AND 2);
    END IF;
    IF NOT EXISTS
    (
        SELECT 1
        FROM information_schema.TABLE_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_messaging_stream_ownership'
          AND CONSTRAINT_NAME = 'CK_fn_messaging_stream_ownership_RollbackState'
          AND CONSTRAINT_TYPE = 'CHECK'
    ) THEN
        ALTER TABLE fn_messaging_stream_ownership
            ADD CONSTRAINT CK_fn_messaging_stream_ownership_RollbackState
                CHECK (RollbackState BETWEEN 0 AND 1);
    END IF;
END$$
DELIMITER ;

CALL fn_messaging_stream_ownership_constraints();
DROP PROCEDURE fn_messaging_stream_ownership_constraints;

INSERT IGNORE INTO fn_messaging_stream_ownership
(
    MessageType, SchemaVersion, TopicCode, CurrentOwner, PreviousOwner,
    CutoffEventId, CutoffOccurredAtUtc, CdcSourcePositionJson, OperatorUserId,
    Reason, RollbackBoundaryEventId, RollbackOccurredAtUtc,
    RollbackState, RollbackGeneration, RollbackPreparedAtUtc,
    CreatedAtUtc, UpdatedAtUtc
)
VALUES
(
    'fullnet.organization.unit.changed', 1, 'organization.unit-changed.v1', 0, 0,
    0x01989abcdef070008000000000000001, UTC_TIMESTAMP(6), NULL, NULL,
    'Initial legacy ownership baseline', NULL, NULL,
    0, NULL, NULL,
    UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)
);
