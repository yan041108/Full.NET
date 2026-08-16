-- 091：追加式 Messaging Outbox 与消费 Inbox。

CREATE TABLE IF NOT EXISTS fn_messaging_outbox_event (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    MessageType varchar(256) NOT NULL COMMENT '消息类型',
    SchemaVersion int NOT NULL COMMENT 'Schema 版本',
    ContentType varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '内容类型',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    PartitionKey varchar(256) NOT NULL COMMENT '分区键',
    CorrelationId varchar(128) NULL COMMENT '关联标识',
    CausationId BINARY(16) NULL COMMENT '因果关联标识',
    TraceParent varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '追踪父级',
    Producer varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '生产者标识',
    Payload longblob NOT NULL COMMENT '消息正文',
    OccurredAtUtc datetime(6) NOT NULL COMMENT '发生时间(UTC)',
    CONSTRAINT PK_fn_messaging_outbox_event PRIMARY KEY (Id),
    CONSTRAINT CK_fn_messaging_outbox_event_SchemaVersion
        CHECK (SchemaVersion > 0),
    KEY IX_fn_messaging_outbox_event_OccurredAtUtc_Id (OccurredAtUtc, Id)
) COMMENT='消息发件箱事件，供 CDC 中继投递' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_messaging_inbox_message (
    ConsumerName varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '消费者名称',
    MessageId BINARY(16) NOT NULL COMMENT '消息标识',
    MessageType varchar(256) NOT NULL COMMENT '消息类型',
    SchemaVersion int NOT NULL COMMENT 'Schema 版本',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    PayloadHash BINARY(32) NOT NULL COMMENT '载荷哈希',
    Status varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '状态',
    Attempts int NOT NULL DEFAULT 0 COMMENT '重试次数',
    ReceivedAtUtc datetime(6) NOT NULL COMMENT '接收时间(UTC)',
    ProcessedAtUtc datetime(6) NULL COMMENT '处理完成时间(UTC)',
    LastErrorCode varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '最后错误码',
    LastError varchar(512) NULL COMMENT '最后错误',
    CONSTRAINT PK_fn_messaging_inbox_message PRIMARY KEY (ConsumerName, MessageId),
    CONSTRAINT CK_fn_messaging_inbox_message_SchemaVersion
        CHECK (SchemaVersion > 0),
    CONSTRAINT CK_fn_messaging_inbox_message_Attempts
        CHECK (Attempts >= 0),
    CONSTRAINT CK_fn_messaging_inbox_message_Status
        CHECK (Status IN ('processing', 'processed', 'failed'))
) COMMENT='消息收件箱，记录消费者幂等处理状态' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

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