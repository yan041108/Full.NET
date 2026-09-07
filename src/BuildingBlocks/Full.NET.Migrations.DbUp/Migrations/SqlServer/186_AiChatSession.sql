-- 186：AI 聊天会话与消息表。

IF OBJECT_ID(N'dbo.fn_ai_chat_session', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_chat_session
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        OwnerUserId uniqueidentifier NOT NULL,
        ModelConfigId uniqueidentifier NOT NULL,
        ModelName nvarchar(128) NOT NULL,
        Title nvarchar(256) NOT NULL,
        MessageCount int NOT NULL
            CONSTRAINT DF_fn_ai_chat_session_MessageCount DEFAULT (0),
        LastMessageAtUtc datetimeoffset(7) NULL,
        IsGenerating bit NOT NULL
            CONSTRAINT DF_fn_ai_chat_session_IsGenerating DEFAULT (0),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_ai_chat_session_Version DEFAULT (1),
        CONSTRAINT PK_fn_ai_chat_session PRIMARY KEY CLUSTERED (Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_chat_session')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工智能对话会话表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_session';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_chat_session')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_session'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_session', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_chat_session')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_session'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_session', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_chat_session')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_session'), N'IsGenerating', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否正在生成', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_session', @level2type=N'COLUMN', @level2name=N'IsGenerating';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_chat_session')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_session'), N'LastMessageAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Last Message At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_session', @level2type=N'COLUMN', @level2name=N'LastMessageAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_chat_session')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_session'), N'MessageCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消息数量', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_session', @level2type=N'COLUMN', @level2name=N'MessageCount';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_chat_session')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_session'), N'ModelConfigId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'模型配置标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_session', @level2type=N'COLUMN', @level2name=N'ModelConfigId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_chat_session')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_session'), N'ModelName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'模型名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_session', @level2type=N'COLUMN', @level2name=N'ModelName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_chat_session')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_session'), N'OwnerUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所有者用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_session', @level2type=N'COLUMN', @level2name=N'OwnerUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_chat_session')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_session'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_session', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_chat_session')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_session'), N'Title', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'标题', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_session', @level2type=N'COLUMN', @level2name=N'Title';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_chat_session')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_session'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_session', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_chat_session')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_session'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_session', @level2type=N'COLUMN', @level2name=N'Version';
END;

IF OBJECT_ID(N'dbo.fn_ai_chat_message', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_chat_message
    (
        Id uniqueidentifier NOT NULL,
        SessionId uniqueidentifier NOT NULL,
        RoleKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Content nvarchar(max) NOT NULL,
        StatusKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PromptTokens int NULL,
        CompletionTokens int NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_ai_chat_message PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_ai_chat_message_RoleKey
            CHECK (RoleKey IN (N'user', N'assistant', N'system')),
        CONSTRAINT CK_fn_ai_chat_message_StatusKey
            CHECK (StatusKey IN (N'streaming', N'completed', N'cancelled', N'failed'))
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_chat_message')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工智能对话消息表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_message';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_chat_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_message'), N'CompletionTokens', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'补全 Token 数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_message', @level2type=N'COLUMN', @level2name=N'CompletionTokens';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_chat_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_message'), N'Content', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'内容', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_message', @level2type=N'COLUMN', @level2name=N'Content';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_chat_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_message'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_message', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_chat_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_message'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_message', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_chat_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_message'), N'PromptTokens', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'提示 Token 数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_message', @level2type=N'COLUMN', @level2name=N'PromptTokens';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_chat_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_message'), N'RoleKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'角色键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_message', @level2type=N'COLUMN', @level2name=N'RoleKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_chat_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_message'), N'SessionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'会话标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_message', @level2type=N'COLUMN', @level2name=N'SessionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_chat_message')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_message'), N'StatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_message', @level2type=N'COLUMN', @level2name=N'StatusKey';
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_ai_chat_session')
      AND name = N'IX_fn_ai_chat_session_OwnerUserId_UpdatedAtUtc')
    CREATE INDEX IX_fn_ai_chat_session_OwnerUserId_UpdatedAtUtc
        ON dbo.fn_ai_chat_session(OwnerUserId, UpdatedAtUtc DESC, Id);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_ai_chat_message')
      AND name = N'IX_fn_ai_chat_message_SessionId_CreatedAtUtc')
    CREATE INDEX IX_fn_ai_chat_message_SessionId_CreatedAtUtc
        ON dbo.fn_ai_chat_message(SessionId, CreatedAtUtc, Id);
