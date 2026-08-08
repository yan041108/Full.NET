-- 091：追加式 Messaging Outbox 与消费 Inbox。

CREATE TABLE IF NOT EXISTS fn_messaging_outbox_event
(
    Id BINARY(16) NOT NULL,
    MessageType varchar(256) NOT NULL,
    SchemaVersion int NOT NULL,
    ContentType varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    TenantId BINARY(16) NULL,
    PartitionKey varchar(256) NOT NULL,
    CorrelationId varchar(128) NULL,
    CausationId BINARY(16) NULL,
    TraceParent varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL,
    Producer varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    Payload longblob NOT NULL,
    OccurredAtUtc datetime(6) NOT NULL,
    CONSTRAINT PK_fn_messaging_outbox_event PRIMARY KEY (Id),
    CONSTRAINT CK_fn_messaging_outbox_event_SchemaVersion
        CHECK (SchemaVersion > 0),
    KEY IX_fn_messaging_outbox_event_OccurredAtUtc_Id (OccurredAtUtc, Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_messaging_inbox_message
(
    ConsumerName varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    MessageId BINARY(16) NOT NULL,
    MessageType varchar(256) NOT NULL,
    SchemaVersion int NOT NULL,
    TenantId BINARY(16) NULL,
    PayloadHash BINARY(32) NOT NULL,
    Status varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    Attempts int NOT NULL DEFAULT 0,
    ReceivedAtUtc datetime(6) NOT NULL,
    ProcessedAtUtc datetime(6) NULL,
    LastErrorCode varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL,
    LastError varchar(512) NULL,
    CONSTRAINT PK_fn_messaging_inbox_message PRIMARY KEY (ConsumerName, MessageId),
    CONSTRAINT CK_fn_messaging_inbox_message_SchemaVersion
        CHECK (SchemaVersion > 0),
    CONSTRAINT CK_fn_messaging_inbox_message_Attempts
        CHECK (Attempts >= 0),
    CONSTRAINT CK_fn_messaging_inbox_message_Status
        CHECK (Status IN ('processing', 'processed', 'failed'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP PROCEDURE IF EXISTS fn_messaging_outbox_event_boundary;
DELIMITER $$
CREATE PROCEDURE fn_messaging_outbox_event_boundary()
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_messaging_outbox_event'
          AND INDEX_NAME = 'IX_fn_messaging_outbox_event_OccurredAtUtc_Id'
    )
    AND
    (
        (
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_messaging_outbox_event'
              AND INDEX_NAME = 'IX_fn_messaging_outbox_event_OccurredAtUtc_Id'
        ) <> 2
        OR EXISTS
        (
            SELECT 1
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_messaging_outbox_event'
              AND INDEX_NAME = 'IX_fn_messaging_outbox_event_OccurredAtUtc_Id'
              AND
              (
                  NON_UNIQUE <> 1
                  OR SUB_PART IS NOT NULL
                  OR (SEQ_IN_INDEX = 1 AND COLUMN_NAME <> 'OccurredAtUtc')
                  OR (SEQ_IN_INDEX = 2 AND COLUMN_NAME <> 'Id')
              )
        )
    ) THEN
        DROP INDEX IX_fn_messaging_outbox_event_OccurredAtUtc_Id
            ON fn_messaging_outbox_event;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_messaging_outbox_event'
          AND INDEX_NAME = 'IX_fn_messaging_outbox_event_OccurredAtUtc_Id'
    ) THEN
        CREATE INDEX IX_fn_messaging_outbox_event_OccurredAtUtc_Id
            ON fn_messaging_outbox_event (OccurredAtUtc, Id);
    END IF;
END$$
DELIMITER ;

CALL fn_messaging_outbox_event_boundary();
DROP PROCEDURE fn_messaging_outbox_event_boundary;