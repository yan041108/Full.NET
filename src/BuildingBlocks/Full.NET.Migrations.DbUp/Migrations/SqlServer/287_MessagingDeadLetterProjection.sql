-- 287：创建式迁移；对象探测支持未记账和索引步骤失败后的重跑。
IF OBJECT_ID(N'dbo.fn_messaging_dead_letter', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_messaging_dead_letter (
        Id uniqueidentifier NOT NULL,
        ConsumerName varchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DlqTopic varchar(249) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DlqPartition int NOT NULL,
        DlqOffset bigint NOT NULL,
        SourceTopic varchar(249) COLLATE Latin1_General_100_BIN2 NOT NULL,
        SourcePartition int NOT NULL,
        SourceOffset bigint NOT NULL,
        MessageId uniqueidentifier NULL,
        MessageType varchar(256) COLLATE Latin1_General_100_BIN2 NULL,
        SchemaVersion int NULL,
        TenantId uniqueidentifier NULL,
        Attempts int NOT NULL,
        ErrorCode varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        FirstFailedAtUtc datetimeoffset(7) NOT NULL,
        LastFailedAtUtc datetimeoffset(7) NOT NULL,
        CanReplay bit NOT NULL,
        ReplayedAtUtc datetimeoffset(7) NULL,
        SnapshotJson nvarchar(max) NOT NULL,
        CONSTRAINT PK_fn_messaging_dead_letter PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_messaging_dead_letter_Position CHECK (DlqPartition >= 0 AND DlqOffset >= 0 AND SourcePartition >= 0 AND SourceOffset >= 0),
        CONSTRAINT CK_fn_messaging_dead_letter_Attempts CHECK (Attempts >= 0)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_messaging_dead_letter') AND name = N'UX_fn_messaging_dead_letter_DlqTopic_DlqPartition_DlqOffset')
    CREATE UNIQUE INDEX UX_fn_messaging_dead_letter_DlqTopic_DlqPartition_DlqOffset ON dbo.fn_messaging_dead_letter (DlqTopic, DlqPartition, DlqOffset);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_messaging_dead_letter') AND name = N'IX_fn_messaging_dead_letter_LastFailedAtUtc_Id')
    CREATE CLUSTERED INDEX IX_fn_messaging_dead_letter_LastFailedAtUtc_Id ON dbo.fn_messaging_dead_letter (LastFailedAtUtc, Id);
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_dead_letter') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消息死信运维投影与不可变恢复快照', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_dead_letter';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_dead_letter') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_dead_letter'), N'Id', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键，由应用生成 UUID v7', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_dead_letter', @level2type=N'COLUMN', @level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_dead_letter') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_dead_letter'), N'ConsumerName', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'原始失败消费者名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_dead_letter', @level2type=N'COLUMN', @level2name=N'ConsumerName';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_dead_letter') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_dead_letter'), N'DlqTopic', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'真实死信 Topic', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_dead_letter', @level2type=N'COLUMN', @level2name=N'DlqTopic';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_dead_letter') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_dead_letter'), N'DlqPartition', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'死信分区编号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_dead_letter', @level2type=N'COLUMN', @level2name=N'DlqPartition';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_dead_letter') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_dead_letter'), N'DlqOffset', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'死信分区中的唯一 Offset', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_dead_letter', @level2type=N'COLUMN', @level2name=N'DlqOffset';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_dead_letter') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_dead_letter'), N'SourceTopic', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'首次失败来源 Topic', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_dead_letter', @level2type=N'COLUMN', @level2name=N'SourceTopic';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_dead_letter') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_dead_letter'), N'SourcePartition', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'首次失败来源分区', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_dead_letter', @level2type=N'COLUMN', @level2name=N'SourcePartition';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_dead_letter') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_dead_letter'), N'SourceOffset', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'首次失败来源 Offset', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_dead_letter', @level2type=N'COLUMN', @level2name=N'SourceOffset';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_dead_letter') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_dead_letter'), N'MessageId', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'合法信封的消息标识，损坏消息可空', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_dead_letter', @level2type=N'COLUMN', @level2name=N'MessageId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_dead_letter') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_dead_letter'), N'MessageType', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'合法信封的稳定消息类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_dead_letter', @level2type=N'COLUMN', @level2name=N'MessageType';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_dead_letter') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_dead_letter'), N'SchemaVersion', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'合法信封的结构版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_dead_letter', @level2type=N'COLUMN', @level2name=N'SchemaVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_dead_letter') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_dead_letter'), N'TenantId', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'原事务可信租户标识，损坏或宿主消息可空', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_dead_letter', @level2type=N'COLUMN', @level2name=N'TenantId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_dead_letter') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_dead_letter'), N'Attempts', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'失败时累计重试次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_dead_letter', @level2type=N'COLUMN', @level2name=N'Attempts';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_dead_letter') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_dead_letter'), N'ErrorCode', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'稳定失败原因码，不保存异常正文', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_dead_letter', @level2type=N'COLUMN', @level2name=N'ErrorCode';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_dead_letter') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_dead_letter'), N'FirstFailedAtUtc', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'首次失败 UTC 时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_dead_letter', @level2type=N'COLUMN', @level2name=N'FirstFailedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_dead_letter') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_dead_letter'), N'LastFailedAtUtc', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近失败 UTC 时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_dead_letter', @level2type=N'COLUMN', @level2name=N'LastFailedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_dead_letter') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_dead_letter'), N'CanReplay', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'信封与消费者可用于受控重放', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_dead_letter', @level2type=N'COLUMN', @level2name=N'CanReplay';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_dead_letter') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_dead_letter'), N'ReplayedAtUtc', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'首次受控重放提交的 UTC 时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_dead_letter', @level2type=N'COLUMN', @level2name=N'ReplayedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_dead_letter') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_dead_letter'), N'SnapshotJson', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'仅供内部重放的不可变消息快照，禁止直接返回客户端', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_dead_letter', @level2type=N'COLUMN', @level2name=N'SnapshotJson';
