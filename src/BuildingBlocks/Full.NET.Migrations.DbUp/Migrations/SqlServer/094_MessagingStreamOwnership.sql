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
