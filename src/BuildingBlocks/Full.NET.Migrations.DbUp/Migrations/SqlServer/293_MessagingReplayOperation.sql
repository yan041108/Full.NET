-- 创建 Host 范围重放状态与检查点；建表及后续索引独立探测，未记账中断可重跑。
IF OBJECT_ID(N'dbo.fn_messaging_replay_operation', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_messaging_replay_operation
    (
        Id uniqueidentifier NOT NULL,
        RequestId uniqueidentifier NOT NULL,
        EventType nvarchar(256) NOT NULL,
        SchemaVersion int NOT NULL,
        TopicCode varchar(128) NOT NULL,
        ConsumerName varchar(128) NOT NULL,
        RequestJson nvarchar(max) NOT NULL,
        SnapshotJson nvarchar(max) NULL,
        State varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        AttemptCount int NOT NULL,
        LeaseToken uniqueidentifier NULL,
        LeaseUntilUtc datetimeoffset(7) NULL,
        InFlightPartition int NULL,
        InFlightOffset bigint NULL,
        ProcessedCount bigint NOT NULL,
        AlreadyProcessedCount bigint NOT NULL,
        RejectedCount bigint NOT NULL,
        LastReasonCode varchar(128) NULL,
        OperatorUserId uniqueidentifier NOT NULL,
        OperatorDisplayName nvarchar(128) NOT NULL,
        Reason nvarchar(512) NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_messaging_replay_operation PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UQ_fn_messaging_replay_operation_RequestId UNIQUE NONCLUSTERED (RequestId),
        CONSTRAINT CK_fn_messaging_replay_operation_State CHECK (State IN ('prepared', 'running', 'partial', 'completed', 'cancel_requested', 'cancelled', 'result_unknown', 'blocked')),
        CONSTRAINT CK_fn_messaging_replay_operation_Counts CHECK (AttemptCount >= 0 AND ProcessedCount >= 0 AND AlreadyProcessedCount >= 0 AND RejectedCount >= 0),
        CONSTRAINT CK_fn_messaging_replay_operation_Intent CHECK ((InFlightPartition IS NULL AND InFlightOffset IS NULL) OR (InFlightPartition >= 0 AND InFlightOffset >= 0)),
        CONSTRAINT CK_fn_messaging_replay_operation_Json CHECK (ISJSON(RequestJson) = 1 AND DATALENGTH(RequestJson) <= 65536 AND (SnapshotJson IS NULL OR (ISJSON(SnapshotJson) = 1 AND DATALENGTH(SnapshotJson) <= 65536)))
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_messaging_replay_operation') AND name = N'IX_fn_messaging_replay_operation_CreatedAtUtc_Id')
    CREATE CLUSTERED INDEX IX_fn_messaging_replay_operation_CreatedAtUtc_Id ON dbo.fn_messaging_replay_operation (CreatedAtUtc, Id);
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_replay_operation') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Host 范围重放操作与可恢复检查点', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_replay_operation';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_replay_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_replay_operation'), N'Id', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'应用生成的重放操作 UUID v7', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_replay_operation', @level2type=N'COLUMN', @level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_replay_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_replay_operation'), N'RequestId', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'客户端创建重试键，不作为逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_replay_operation', @level2type=N'COLUMN', @level2name=N'RequestId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_replay_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_replay_operation'), N'EventType', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'权威目录中的事件类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_replay_operation', @level2type=N'COLUMN', @level2name=N'EventType';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_replay_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_replay_operation'), N'SchemaVersion', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'权威目录中的事件版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_replay_operation', @level2type=N'COLUMN', @level2name=N'SchemaVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_replay_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_replay_operation'), N'TopicCode', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'目标 Topic 机器码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_replay_operation', @level2type=N'COLUMN', @level2name=N'TopicCode';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_replay_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_replay_operation'), N'ConsumerName', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'原业务 Inbox 订阅名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_replay_operation', @level2type=N'COLUMN', @level2name=N'ConsumerName';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_replay_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_replay_operation'), N'RequestJson', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'不可变的规范化请求，不包含载荷或凭据', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_replay_operation', @level2type=N'COLUMN', @level2name=N'RequestJson';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_replay_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_replay_operation'), N'SnapshotJson', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定资源和分区窗口及未确认意图', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_replay_operation', @level2type=N'COLUMN', @level2name=N'SnapshotJson';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_replay_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_replay_operation'), N'State', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作稳定状态码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_replay_operation', @level2type=N'COLUMN', @level2name=N'State';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_replay_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_replay_operation'), N'AttemptCount', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已开始的执行代次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_replay_operation', @level2type=N'COLUMN', @level2name=N'AttemptCount';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_replay_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_replay_operation'), N'LeaseToken', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前或最近执行代次标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_replay_operation', @level2type=N'COLUMN', @level2name=N'LeaseToken';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_replay_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_replay_operation'), N'LeaseUntilUtc', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'执行租约失效的 UTC 时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_replay_operation', @level2type=N'COLUMN', @level2name=N'LeaseUntilUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_replay_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_replay_operation'), N'InFlightPartition', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近尚未确认消息的分区号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_replay_operation', @level2type=N'COLUMN', @level2name=N'InFlightPartition';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_replay_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_replay_operation'), N'InFlightOffset', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近尚未确认消息的 Broker 位点', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_replay_operation', @level2type=N'COLUMN', @level2name=N'InFlightOffset';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_replay_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_replay_operation'), N'ProcessedCount', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已持久确认的新业务提交数量', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_replay_operation', @level2type=N'COLUMN', @level2name=N'ProcessedCount';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_replay_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_replay_operation'), N'AlreadyProcessedCount', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已持久确认的 Inbox 去重数量', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_replay_operation', @level2type=N'COLUMN', @level2name=N'AlreadyProcessedCount';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_replay_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_replay_operation'), N'RejectedCount', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已确认拒绝的尝试次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_replay_operation', @level2type=N'COLUMN', @level2name=N'RejectedCount';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_replay_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_replay_operation'), N'LastReasonCode', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近状态原因的稳定机器码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_replay_operation', @level2type=N'COLUMN', @level2name=N'LastReasonCode';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_replay_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_replay_operation'), N'OperatorUserId', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建操作的可信用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_replay_operation', @level2type=N'COLUMN', @level2name=N'OperatorUserId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_replay_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_replay_operation'), N'OperatorDisplayName', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时的操作者显示名快照', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_replay_operation', @level2type=N'COLUMN', @level2name=N'OperatorDisplayName';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_replay_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_replay_operation'), N'Reason', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建操作时的不可变运维理由', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_replay_operation', @level2type=N'COLUMN', @level2name=N'Reason';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_replay_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_replay_operation'), N'CreatedAtUtc', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作创建的 UTC 时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_replay_operation', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_replay_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_replay_operation'), N'UpdatedAtUtc', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近持久进度的 UTC 时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_replay_operation', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
