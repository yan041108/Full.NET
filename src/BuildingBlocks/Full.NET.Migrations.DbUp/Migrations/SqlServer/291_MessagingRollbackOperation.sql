-- 操作记录与生产者保护同事务建立；DDL 各阶段可独立重跑。
IF OBJECT_ID(N'dbo.fn_messaging_rollback_operation', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_messaging_rollback_operation
    (
        Id uniqueidentifier NOT NULL,
        EventType nvarchar(256) NOT NULL,
        SchemaVersion int NOT NULL,
        Phase int NOT NULL,
        ResourcesJson nvarchar(max) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        HasPauseIntent bit NOT NULL CONSTRAINT DF_fn_messaging_rollback_operation_HasPauseIntent DEFAULT (0),
        OperatorUserId uniqueidentifier NULL,
        Reason nvarchar(512) NULL,
        CONSTRAINT PK_fn_messaging_rollback_operation PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_messaging_rollback_operation_Phase CHECK (Phase BETWEEN 0 AND 5),
        CONSTRAINT CK_fn_messaging_rollback_operation_SchemaVersion CHECK (SchemaVersion > 0)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_messaging_rollback_operation') AND name = N'IX_fn_messaging_rollback_operation_CreatedAtUtc_Id')
    CREATE CLUSTERED INDEX IX_fn_messaging_rollback_operation_CreatedAtUtc_Id
    ON dbo.fn_messaging_rollback_operation (CreatedAtUtc, Id);

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_rollback_operation') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'事件交付回退操作与控制面恢复日志', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_rollback_operation';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_rollback_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_rollback_operation'), N'Id', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'应用生成的回退代次 UUID v7', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_rollback_operation', @level2type=N'COLUMN', @level2name=N'Id';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_rollback_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_rollback_operation'), N'EventType', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作所属事件流的稳定消息类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_rollback_operation', @level2type=N'COLUMN', @level2name=N'EventType';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_rollback_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_rollback_operation'), N'SchemaVersion', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'事件流结构版本，必须大于零', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_rollback_operation', @level2type=N'COLUMN', @level2name=N'SchemaVersion';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_rollback_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_rollback_operation'), N'Phase', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'持久回退状态，包含外部动作意图与最终结果', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_rollback_operation', @level2type=N'COLUMN', @level2name=N'Phase';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_rollback_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_rollback_operation'), N'ResourcesJson', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'暂停前冻结的控制资源快照，禁止向客户端返回', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_rollback_operation', @level2type=N'COLUMN', @level2name=N'ResourcesJson';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_rollback_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_rollback_operation'), N'CreatedAtUtc', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作创建的 UTC 时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_rollback_operation', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_rollback_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_rollback_operation'), N'UpdatedAtUtc', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近一次状态迁移的 UTC 时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_rollback_operation', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_rollback_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_rollback_operation'), N'HasPauseIntent', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'曾经持久化暂停意图，恢复时不可丢失', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_rollback_operation', @level2type=N'COLUMN', @level2name=N'HasPauseIntent';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_rollback_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_rollback_operation'), N'OperatorUserId', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'建立回退操作的可信操作者标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_rollback_operation', @level2type=N'COLUMN', @level2name=N'OperatorUserId';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_rollback_operation') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_rollback_operation'), N'Reason', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'建立回退操作的不可变运维理由', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_rollback_operation', @level2type=N'COLUMN', @level2name=N'Reason';
