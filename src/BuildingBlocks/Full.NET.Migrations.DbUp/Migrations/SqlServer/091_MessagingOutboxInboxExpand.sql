-- 091：追加式 Messaging Outbox 与消费 Inbox（高写入 Outbox：非聚集主键 + 时间路径聚集索引）。

IF OBJECT_ID(N'dbo.fn_messaging_outbox_event', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_messaging_outbox_event
    (
        Id uniqueidentifier NOT NULL,
        MessageType nvarchar(256) NOT NULL,
        SchemaVersion int NOT NULL,
        ContentType varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        TenantId uniqueidentifier NULL,
        PartitionKey nvarchar(256) NOT NULL,
        CorrelationId nvarchar(128) NULL,
        CausationId uniqueidentifier NULL,
        TraceParent varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
        Producer varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Payload varbinary(max) NOT NULL,
        OccurredAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_messaging_outbox_event PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_messaging_outbox_event_SchemaVersion
            CHECK (SchemaVersion > 0)
    );
END;

IF OBJECT_ID(N'dbo.fn_messaging_inbox_message', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_messaging_inbox_message
    (
        ConsumerName varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        MessageId uniqueidentifier NOT NULL,
        MessageType nvarchar(256) NOT NULL,
        SchemaVersion int NOT NULL,
        TenantId uniqueidentifier NULL,
        PayloadHash varbinary(32) NOT NULL,
        Status varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Attempts int NOT NULL
            CONSTRAINT DF_fn_messaging_inbox_message_Attempts DEFAULT (0),
        ReceivedAtUtc datetimeoffset(7) NOT NULL,
        ProcessedAtUtc datetimeoffset(7) NULL,
        LastErrorCode varchar(128) COLLATE Latin1_General_100_BIN2 NULL,
        LastError nvarchar(512) NULL,
        CONSTRAINT PK_fn_messaging_inbox_message PRIMARY KEY CLUSTERED (ConsumerName, MessageId),
        CONSTRAINT CK_fn_messaging_inbox_message_SchemaVersion
            CHECK (SchemaVersion > 0),
        CONSTRAINT CK_fn_messaging_inbox_message_Attempts
            CHECK (Attempts >= 0),
        CONSTRAINT CK_fn_messaging_inbox_message_Status
            CHECK (Status IN ('processing', 'processed', 'failed'))
    );
END;

IF EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_messaging_outbox_event')
      AND indexObject.name = N'IX_fn_messaging_outbox_event_OccurredAtUtc_Id'
      AND
      (
          indexObject.is_unique = 1
          OR indexObject.type <> 1
          OR indexObject.is_disabled = 1
          OR
          (
              SELECT COUNT(*)
              FROM sys.index_columns AS indexColumn
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal > 0
          ) <> 2
          OR NOT EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS indexColumn
              INNER JOIN sys.columns AS columnObject
                  ON columnObject.object_id = indexColumn.object_id
                 AND columnObject.column_id = indexColumn.column_id
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal = 1
                AND columnObject.name = N'OccurredAtUtc'
          )
          OR NOT EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS indexColumn
              INNER JOIN sys.columns AS columnObject
                  ON columnObject.object_id = indexColumn.object_id
                 AND columnObject.column_id = indexColumn.column_id
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal = 2
                AND columnObject.name = N'Id'
          )
      )
)
BEGIN
    DROP INDEX IX_fn_messaging_outbox_event_OccurredAtUtc_Id
        ON dbo.fn_messaging_outbox_event;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_messaging_outbox_event')
      AND name = N'IX_fn_messaging_outbox_event_OccurredAtUtc_Id'
)
BEGIN
    CREATE CLUSTERED INDEX IX_fn_messaging_outbox_event_OccurredAtUtc_Id
        ON dbo.fn_messaging_outbox_event(OccurredAtUtc, Id);
END;