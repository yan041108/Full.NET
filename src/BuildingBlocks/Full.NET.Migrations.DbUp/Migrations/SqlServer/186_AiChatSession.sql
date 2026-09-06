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
