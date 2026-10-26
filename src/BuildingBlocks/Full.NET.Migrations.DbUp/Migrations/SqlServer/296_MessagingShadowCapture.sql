-- 296：真实业务的有界影子采集，独立于正式 CDC 发布源。
IF OBJECT_ID(N'dbo.fn_messaging_shadow_capture', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_messaging_shadow_capture (
        Id uniqueidentifier NOT NULL,
        Generation uniqueidentifier NOT NULL,
        StartRequestId uniqueidentifier NOT NULL,
        MessageType varchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
        SchemaVersion int NOT NULL,
        State varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        MaxEvents int NOT NULL,
        MaxPayloadBytes bigint NOT NULL,
        CapturedCount int NOT NULL,
        CapturedBytes bigint NOT NULL,
        MissingMetadataCount int NOT NULL,
        LastReservedEventId uniqueidentifier NULL,
        StartedAtUtc datetimeoffset(7) NOT NULL,
        ExpiresAtUtc datetimeoffset(7) NOT NULL,
        ClosedAtUtc datetimeoffset(7) NULL,
        EndReason varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        OperatorUserId uniqueidentifier NOT NULL,
        OperatorDisplayName nvarchar(128) NOT NULL,
        Reason nvarchar(512) NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        StartExpectedGeneration uniqueidentifier NULL,
        CONSTRAINT PK_fn_messaging_shadow_capture PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UX_fn_messaging_shadow_capture_Generation UNIQUE (Generation),
        CONSTRAINT UX_fn_messaging_shadow_capture_MessageType_SchemaVersion UNIQUE (MessageType, SchemaVersion),
        CONSTRAINT CK_fn_messaging_shadow_capture_Budget CHECK (SchemaVersion > 0 AND MaxEvents BETWEEN 1 AND 10000 AND MaxPayloadBytes BETWEEN 1 AND 33554432 AND CapturedCount BETWEEN 0 AND MaxEvents AND CapturedBytes BETWEEN 0 AND MaxPayloadBytes AND MissingMetadataCount >= 0),
        CONSTRAINT CK_fn_messaging_shadow_capture_State CHECK (State IN ('active', 'closed', 'blocked'))
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_capture') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'按流唯一的受控影子采集会话', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_capture';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_capture') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_capture'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'采集控制记录的应用端 UUID v7 主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_capture', @level2type=N'COLUMN', @level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_capture') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_capture'), N'Generation', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前采集代次，重新开始时生成新 UUID v7', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_capture', @level2type=N'COLUMN', @level2name=N'Generation';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_capture') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_capture'), N'StartExpectedGeneration', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'开始请求绑定的上一代，初次为空；用于完整幂等正文核对', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_capture', @level2type=N'COLUMN', @level2name=N'StartExpectedGeneration';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_capture') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_capture'), N'StartRequestId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'开始采集的幂等请求键，重试必须复用相同预期代次和正文', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_capture', @level2type=N'COLUMN', @level2name=N'StartRequestId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_capture') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_capture'), N'MessageType', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已登记的试点事件类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_capture', @level2type=N'COLUMN', @level2name=N'MessageType';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_capture') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_capture'), N'SchemaVersion', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已登记的事件版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_capture', @level2type=N'COLUMN', @level2name=N'SchemaVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_capture') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_capture'), N'State', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'采集状态 active、closed 或 blocked', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_capture', @level2type=N'COLUMN', @level2name=N'State';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_capture') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_capture'), N'MaxEvents', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前代次固定的消息预算', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_capture', @level2type=N'COLUMN', @level2name=N'MaxEvents';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_capture') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_capture'), N'MaxPayloadBytes', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前代次固定的载荷字节预算', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_capture', @level2type=N'COLUMN', @level2name=N'MaxPayloadBytes';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_capture') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_capture'), N'CapturedCount', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'与影子记录同事务确认的采集数量', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_capture', @level2type=N'COLUMN', @level2name=N'CapturedCount';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_capture') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_capture'), N'CapturedBytes', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已采集真实载荷字节总数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_capture', @level2type=N'COLUMN', @level2name=N'CapturedBytes';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_capture') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_capture'), N'MissingMetadataCount', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'采集窗口内缺少元数据的业务写入数量', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_capture', @level2type=N'COLUMN', @level2name=N'MissingMetadataCount';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_capture') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_capture'), N'LastReservedEventId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前事务最后预留的真实 Legacy 消息标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_capture', @level2type=N'COLUMN', @level2name=N'LastReservedEventId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_capture') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_capture'), N'StartedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前代次开始时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_capture', @level2type=N'COLUMN', @level2name=N'StartedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_capture') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_capture'), N'ExpiresAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前代次不可延长的截止时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_capture', @level2type=N'COLUMN', @level2name=N'ExpiresAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_capture') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_capture'), N'ClosedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际关闭或阻断时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_capture', @level2type=N'COLUMN', @level2name=N'ClosedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_capture') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_capture'), N'EndReason', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'关闭或阻断的稳定原因码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_capture', @level2type=N'COLUMN', @level2name=N'EndReason';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_capture') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_capture'), N'OperatorUserId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'由可信认证上下文取得的操作者', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_capture', @level2type=N'COLUMN', @level2name=N'OperatorUserId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_capture') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_capture'), N'OperatorDisplayName', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作者显示名快照', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_capture', @level2type=N'COLUMN', @level2name=N'OperatorDisplayName';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_capture') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_capture'), N'Reason', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'开始本代采集的人工理由', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_capture', @level2type=N'COLUMN', @level2name=N'Reason';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_capture') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_capture'), N'CreatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'控制记录创建时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_capture', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_capture') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_capture'), N'UpdatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'控制记录最近更新时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_capture', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';

IF OBJECT_ID(N'dbo.fn_messaging_shadow_event', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_messaging_shadow_event (
        Id uniqueidentifier NOT NULL,
        CaptureGeneration uniqueidentifier NOT NULL,
        MessageType varchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
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
        CONSTRAINT PK_fn_messaging_shadow_event PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_messaging_shadow_event_Version CHECK (SchemaVersion > 0)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_messaging_shadow_event') AND name = N'IX_fn_messaging_shadow_event_OccurredAtUtc_Id')
    CREATE CLUSTERED INDEX IX_fn_messaging_shadow_event_OccurredAtUtc_Id ON dbo.fn_messaging_shadow_event (OccurredAtUtc, Id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_messaging_shadow_event') AND name = N'IX_fn_messaging_shadow_event_CaptureGeneration_Id')
    CREATE NONCLUSTERED INDEX IX_fn_messaging_shadow_event_CaptureGeneration_Id ON dbo.fn_messaging_shadow_event (CaptureGeneration, Id);
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_event') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'独立影子源；禁止作为正式业务发布源', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_event';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_event') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_event'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'原 Legacy 消息的 UUID v7，影子不另行生成身份', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_event', @level2type=N'COLUMN', @level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_event') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_event'), N'CaptureGeneration', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'来源采集代次，仅供隔离影子样本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_event', @level2type=N'COLUMN', @level2name=N'CaptureGeneration';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_event') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_event'), N'MessageType', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'原始业务事件类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_event', @level2type=N'COLUMN', @level2name=N'MessageType';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_event') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_event'), N'SchemaVersion', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'原始业务事件版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_event', @level2type=N'COLUMN', @level2name=N'SchemaVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_event') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_event'), N'ContentType', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'原始序列化内容类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_event', @level2type=N'COLUMN', @level2name=N'ContentType';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_event') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_event'), N'TenantId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'原业务可信租户上下文', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_event', @level2type=N'COLUMN', @level2name=N'TenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_event') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_event'), N'PartitionKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'真实业务分区键，禁止临时伪造', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_event', @level2type=N'COLUMN', @level2name=N'PartitionKey';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_event') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_event'), N'CorrelationId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'原业务关联标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_event', @level2type=N'COLUMN', @level2name=N'CorrelationId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_event') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_event'), N'CausationId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'原始因果消息标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_event', @level2type=N'COLUMN', @level2name=N'CausationId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_event') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_event'), N'TraceParent', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'经格式校验的 W3C 链路上下文', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_event', @level2type=N'COLUMN', @level2name=N'TraceParent';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_event') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_event'), N'Producer', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'真实业务生产者代码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_event', @level2type=N'COLUMN', @level2name=N'Producer';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_event') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_event'), N'Payload', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'原 Legacy 已序列化的同一份业务字节', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_event', @level2type=N'COLUMN', @level2name=N'Payload';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_messaging_shadow_event') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_shadow_event'), N'OccurredAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'原 Legacy 消息发生时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_shadow_event', @level2type=N'COLUMN', @level2name=N'OccurredAtUtc';
