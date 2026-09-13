-- 212：Agent 持久运行、步骤、检查点与事件；expand-only，支持幂等重跑与租约 fencing。
IF OBJECT_ID(N'dbo.fn_ai_agent_run', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_agent_run (
        Id uniqueidentifier NOT NULL,
        ScopeKey varchar(32) NOT NULL,
        TenantId uniqueidentifier NULL,
        ActorUserId uniqueidentifier NOT NULL,
        SessionId uniqueidentifier NOT NULL,
        ClientRequestId uniqueidentifier NOT NULL,
        RequestHash varchar(64) NOT NULL,
        DefinitionKey varchar(64) NOT NULL,
        DefinitionVersion int NOT NULL,
        AuthorizationBindingId uniqueidentifier NOT NULL,
        SecurityStamp varchar(64) NOT NULL,
        ActorScope varchar(64) NOT NULL,
        EffectiveScope varchar(64) NOT NULL,
        StatusKey varchar(32) NOT NULL,
        BudgetJson nvarchar(max) NOT NULL,
        DeadlineAtUtc datetimeoffset(7) NOT NULL,
        Version bigint NOT NULL CONSTRAINT DF_fn_ai_agent_run_Version DEFAULT (1),
        LeaseOwner varchar(128) NULL,
        LeaseEpoch bigint NOT NULL CONSTRAINT DF_fn_ai_agent_run_LeaseEpoch DEFAULT (0),
        LeaseExpiresAtUtc datetimeoffset(7) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_ai_agent_run PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_ai_agent_run_StatusKey CHECK (StatusKey IN (
            N'queued', N'running', N'completed', N'awaiting_approval', N'authorization_required',
            N'retry_scheduled', N'reconciliation_required', N'failed', N'cancelled', N'expired'))
    );

END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_agent_run') AND name = N'UX_fn_ai_agent_run_Client')
    CREATE UNIQUE NONCLUSTERED INDEX UX_fn_ai_agent_run_Client ON dbo.fn_ai_agent_run (ScopeKey, ActorUserId, ClientRequestId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_agent_run') AND name = N'IX_fn_ai_agent_run_CreatedAt')
    CREATE CLUSTERED INDEX IX_fn_ai_agent_run_CreatedAt ON dbo.fn_ai_agent_run (CreatedAtUtc, Id);
IF OBJECT_ID(N'dbo.fn_ai_agent_step', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_agent_step (
        Id uniqueidentifier NOT NULL,
        RunId uniqueidentifier NOT NULL,
        StepKey varchar(64) NOT NULL,
        Attempt int NOT NULL,
        OperationId uniqueidentifier NOT NULL,
        StatusKey varchar(16) NOT NULL,
        ModelConfigVersion int NULL,
        ToolVersion int NULL,
        PriceVersionId uniqueidentifier NULL,
        InputTokens bigint NULL,
        OutputTokens bigint NULL,
        Cost decimal(20,8) NULL,
        Currency varchar(3) NULL,
        TraceId varchar(32) NULL,
        ArgumentDigest varchar(64) NULL,
        ErrorCode varchar(64) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_ai_agent_step PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_ai_agent_step_StatusKey CHECK (StatusKey IN (N'started', N'committed', N'failed'))
    );

END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_agent_step') AND name = N'UX_fn_ai_agent_step_Attempt')
    CREATE UNIQUE NONCLUSTERED INDEX UX_fn_ai_agent_step_Attempt ON dbo.fn_ai_agent_step (RunId, StepKey, Attempt);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_agent_step') AND name = N'UX_fn_ai_agent_step_Operation')
    CREATE UNIQUE NONCLUSTERED INDEX UX_fn_ai_agent_step_Operation ON dbo.fn_ai_agent_step (OperationId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_agent_step') AND name = N'IX_fn_ai_agent_step_CreatedAt')
    CREATE CLUSTERED INDEX IX_fn_ai_agent_step_CreatedAt ON dbo.fn_ai_agent_step (CreatedAtUtc, Id);
IF OBJECT_ID(N'dbo.fn_ai_agent_checkpoint', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_agent_checkpoint (
        Id uniqueidentifier NOT NULL,
        RunId uniqueidentifier NOT NULL,
        Sequence bigint NOT NULL,
        FormatVersion int NOT NULL,
        FrameworkVersion varchar(16) NOT NULL,
        DefinitionVersion int NOT NULL,
        PayloadProtected nvarchar(max) NOT NULL,
        Checksum varchar(64) NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_ai_agent_checkpoint PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UX_fn_ai_agent_checkpoint_Sequence UNIQUE NONCLUSTERED (RunId, Sequence)
    );

END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_agent_checkpoint') AND name = N'IX_fn_ai_agent_checkpoint_CreatedAt')
    CREATE CLUSTERED INDEX IX_fn_ai_agent_checkpoint_CreatedAt ON dbo.fn_ai_agent_checkpoint (CreatedAtUtc, Id);
IF OBJECT_ID(N'dbo.fn_ai_agent_event', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_agent_event (
        Id uniqueidentifier NOT NULL,
        RunId uniqueidentifier NOT NULL,
        Sequence bigint NOT NULL,
        EventType varchar(64) NOT NULL,
        PayloadVersion int NOT NULL,
        Payload nvarchar(max) NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_ai_agent_event PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT UX_fn_ai_agent_event_Sequence UNIQUE NONCLUSTERED (RunId, Sequence)
    );

END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_agent_event') AND name = N'IX_fn_ai_agent_event_CreatedAt')
    CREATE CLUSTERED INDEX IX_fn_ai_agent_event_CreatedAt ON dbo.fn_ai_agent_event (CreatedAtUtc, Id);
