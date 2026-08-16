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
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消息发件箱事件，供 CDC 中继投递', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_outbox_event';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'因果关联标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_outbox_event', @level2type=N'COLUMN', @level2name=N'CausationId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'内容类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_outbox_event', @level2type=N'COLUMN', @level2name=N'ContentType';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'关联标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_outbox_event', @level2type=N'COLUMN', @level2name=N'CorrelationId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_outbox_event', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消息类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_outbox_event', @level2type=N'COLUMN', @level2name=N'MessageType';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发生时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_outbox_event', @level2type=N'COLUMN', @level2name=N'OccurredAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'分区键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_outbox_event', @level2type=N'COLUMN', @level2name=N'PartitionKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消息正文', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_outbox_event', @level2type=N'COLUMN', @level2name=N'Payload';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'生产者标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_outbox_event', @level2type=N'COLUMN', @level2name=N'Producer';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Schema 版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_outbox_event', @level2type=N'COLUMN', @level2name=N'SchemaVersion';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_outbox_event', @level2type=N'COLUMN', @level2name=N'TenantId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'追踪父级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_outbox_event', @level2type=N'COLUMN', @level2name=N'TraceParent';
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
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消息收件箱，记录消费者幂等处理状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_inbox_message';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'重试次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_inbox_message', @level2type=N'COLUMN', @level2name=N'Attempts';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消费者名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_inbox_message', @level2type=N'COLUMN', @level2name=N'ConsumerName';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最后错误', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_inbox_message', @level2type=N'COLUMN', @level2name=N'LastError';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最后错误码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_inbox_message', @level2type=N'COLUMN', @level2name=N'LastErrorCode';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消息标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_inbox_message', @level2type=N'COLUMN', @level2name=N'MessageId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消息类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_inbox_message', @level2type=N'COLUMN', @level2name=N'MessageType';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'载荷哈希', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_inbox_message', @level2type=N'COLUMN', @level2name=N'PayloadHash';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'处理完成时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_inbox_message', @level2type=N'COLUMN', @level2name=N'ProcessedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'接收时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_inbox_message', @level2type=N'COLUMN', @level2name=N'ReceivedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Schema 版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_inbox_message', @level2type=N'COLUMN', @level2name=N'SchemaVersion';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_inbox_message', @level2type=N'COLUMN', @level2name=N'Status';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_inbox_message', @level2type=N'COLUMN', @level2name=N'TenantId';
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