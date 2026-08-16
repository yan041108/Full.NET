-- 094：事件流交付所有权持久化记录（试点切流/回退边界）。

IF OBJECT_ID(N'dbo.fn_messaging_stream_ownership', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_messaging_stream_ownership
    (
        MessageType varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        SchemaVersion int NOT NULL,
        TopicCode varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CurrentOwner tinyint NOT NULL,
        PreviousOwner tinyint NOT NULL,
        CutoffEventId uniqueidentifier NOT NULL,
        CutoffOccurredAtUtc datetimeoffset(7) NOT NULL,
        CdcSourcePositionJson nvarchar(max) NULL,
        OperatorUserId uniqueidentifier NULL,
        Reason nvarchar(512) NOT NULL,
        RollbackBoundaryEventId uniqueidentifier NULL,
        RollbackOccurredAtUtc datetimeoffset(7) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_messaging_stream_ownership PRIMARY KEY CLUSTERED (MessageType, SchemaVersion),
        CONSTRAINT CK_fn_messaging_stream_ownership_SchemaVersion CHECK (SchemaVersion BETWEEN 1 AND 65535),
        CONSTRAINT CK_fn_messaging_stream_ownership_CurrentOwner CHECK (CurrentOwner BETWEEN 0 AND 2),
        CONSTRAINT CK_fn_messaging_stream_ownership_PreviousOwner CHECK (PreviousOwner BETWEEN 0 AND 2)
    );
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消息流发布所有权与回滚边界', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_stream_ownership';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'CDC 源位置(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_stream_ownership', @level2type=N'COLUMN', @level2name=N'CdcSourcePositionJson';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_stream_ownership', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前所有者', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_stream_ownership', @level2type=N'COLUMN', @level2name=N'CurrentOwner';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'截止事件标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_stream_ownership', @level2type=N'COLUMN', @level2name=N'CutoffEventId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'截止事件发生时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_stream_ownership', @level2type=N'COLUMN', @level2name=N'CutoffOccurredAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消息类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_stream_ownership', @level2type=N'COLUMN', @level2name=N'MessageType';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_stream_ownership', @level2type=N'COLUMN', @level2name=N'OperatorUserId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'上一任所有者', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_stream_ownership', @level2type=N'COLUMN', @level2name=N'PreviousOwner';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'原因说明', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_stream_ownership', @level2type=N'COLUMN', @level2name=N'Reason';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'回滚边界事件标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_stream_ownership', @level2type=N'COLUMN', @level2name=N'RollbackBoundaryEventId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'回滚发生时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_stream_ownership', @level2type=N'COLUMN', @level2name=N'RollbackOccurredAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Schema 版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_stream_ownership', @level2type=N'COLUMN', @level2name=N'SchemaVersion';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'主题编码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_stream_ownership', @level2type=N'COLUMN', @level2name=N'TopicCode';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_stream_ownership', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
END;
ELSE
BEGIN
    -- 表已存在时按契约收敛列/约束形状，保证重跑迁移可修复部署漂移。
    IF NOT EXISTS (SELECT * FROM sys.check_constraints WHERE name = N'CK_fn_messaging_stream_ownership_SchemaVersion')
    BEGIN
        ALTER TABLE dbo.fn_messaging_stream_ownership
            ADD CONSTRAINT CK_fn_messaging_stream_ownership_SchemaVersion CHECK (SchemaVersion BETWEEN 1 AND 65535);
    END;
    IF NOT EXISTS (SELECT * FROM sys.check_constraints WHERE name = N'CK_fn_messaging_stream_ownership_CurrentOwner')
    BEGIN
        ALTER TABLE dbo.fn_messaging_stream_ownership
            ADD CONSTRAINT CK_fn_messaging_stream_ownership_CurrentOwner CHECK (CurrentOwner BETWEEN 0 AND 2);
    END;
    IF NOT EXISTS (SELECT * FROM sys.check_constraints WHERE name = N'CK_fn_messaging_stream_ownership_PreviousOwner')
    BEGIN
        ALTER TABLE dbo.fn_messaging_stream_ownership
            ADD CONSTRAINT CK_fn_messaging_stream_ownership_PreviousOwner CHECK (PreviousOwner BETWEEN 0 AND 2);
    END;
END;
