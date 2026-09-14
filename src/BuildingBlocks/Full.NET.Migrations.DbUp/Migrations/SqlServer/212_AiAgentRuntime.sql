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
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_run')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工智能agent run表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_run';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_run'), N'ActorScope', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Actor Scope', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_run', @level2type=N'COLUMN', @level2name=N'ActorScope';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_run'), N'ActorUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作者用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_run', @level2type=N'COLUMN', @level2name=N'ActorUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_run'), N'AuthorizationBindingId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Authorization Binding标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_run', @level2type=N'COLUMN', @level2name=N'AuthorizationBindingId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_run'), N'BudgetJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Budget(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_run', @level2type=N'COLUMN', @level2name=N'BudgetJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_run'), N'ClientRequestId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Client Request标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_run', @level2type=N'COLUMN', @level2name=N'ClientRequestId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_run'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_run', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_run'), N'DeadlineAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Deadline At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_run', @level2type=N'COLUMN', @level2name=N'DeadlineAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_run'), N'DefinitionKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'定义稳定键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_run', @level2type=N'COLUMN', @level2name=N'DefinitionKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_run'), N'DefinitionVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Definition Version', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_run', @level2type=N'COLUMN', @level2name=N'DefinitionVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_run'), N'EffectiveScope', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Effective Scope', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_run', @level2type=N'COLUMN', @level2name=N'EffectiveScope';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_run'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_run', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_run'), N'LeaseEpoch', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Lease Epoch', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_run', @level2type=N'COLUMN', @level2name=N'LeaseEpoch';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_run'), N'LeaseExpiresAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租约过期时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_run', @level2type=N'COLUMN', @level2name=N'LeaseExpiresAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_run'), N'LeaseOwner', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Lease Owner', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_run', @level2type=N'COLUMN', @level2name=N'LeaseOwner';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_run'), N'RequestHash', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完整请求绑定摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_run', @level2type=N'COLUMN', @level2name=N'RequestHash';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_run'), N'ScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_run', @level2type=N'COLUMN', @level2name=N'ScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_run'), N'SecurityStamp', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'安全戳', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_run', @level2type=N'COLUMN', @level2name=N'SecurityStamp';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_run'), N'SessionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'会话标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_run', @level2type=N'COLUMN', @level2name=N'SessionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_run'), N'StatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_run', @level2type=N'COLUMN', @level2name=N'StatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_run'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_run', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_run'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_run', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_run'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_run', @level2type=N'COLUMN', @level2name=N'Version';

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
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_step')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工智能agent step表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_step';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_step'), N'ArgumentDigest', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Argument Digest', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_step', @level2type=N'COLUMN', @level2name=N'ArgumentDigest';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_step'), N'Attempt', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Attempt', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_step', @level2type=N'COLUMN', @level2name=N'Attempt';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_step'), N'Cost', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Cost', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_step', @level2type=N'COLUMN', @level2name=N'Cost';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_step'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_step', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_step'), N'Currency', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'币种', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_step', @level2type=N'COLUMN', @level2name=N'Currency';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_step'), N'ErrorCode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'错误码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_step', @level2type=N'COLUMN', @level2name=N'ErrorCode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_step'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_step', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_step'), N'InputTokens', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Input Tokens', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_step', @level2type=N'COLUMN', @level2name=N'InputTokens';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_step'), N'ModelConfigVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Model Config Version', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_step', @level2type=N'COLUMN', @level2name=N'ModelConfigVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_step'), N'OperationId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Operation标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_step', @level2type=N'COLUMN', @level2name=N'OperationId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_step'), N'OutputTokens', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Output Tokens', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_step', @level2type=N'COLUMN', @level2name=N'OutputTokens';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_step'), N'PriceVersionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'价格版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_step', @level2type=N'COLUMN', @level2name=N'PriceVersionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_step'), N'RunId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'运行标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_step', @level2type=N'COLUMN', @level2name=N'RunId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_step'), N'StatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_step', @level2type=N'COLUMN', @level2name=N'StatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_step'), N'StepKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Step Key', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_step', @level2type=N'COLUMN', @level2name=N'StepKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_step'), N'ToolVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Tool Version', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_step', @level2type=N'COLUMN', @level2name=N'ToolVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_step'), N'TraceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'追踪标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_step', @level2type=N'COLUMN', @level2name=N'TraceId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_step')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_step'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_step', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';

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
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_checkpoint')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工智能agent checkpoint表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_checkpoint';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_checkpoint')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_checkpoint'), N'Checksum', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Checksum', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_checkpoint', @level2type=N'COLUMN', @level2name=N'Checksum';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_checkpoint')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_checkpoint'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_checkpoint', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_checkpoint')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_checkpoint'), N'DefinitionVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Definition Version', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_checkpoint', @level2type=N'COLUMN', @level2name=N'DefinitionVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_checkpoint')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_checkpoint'), N'FormatVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Format Version', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_checkpoint', @level2type=N'COLUMN', @level2name=N'FormatVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_checkpoint')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_checkpoint'), N'FrameworkVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Framework Version', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_checkpoint', @level2type=N'COLUMN', @level2name=N'FrameworkVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_checkpoint')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_checkpoint'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_checkpoint', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_checkpoint')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_checkpoint'), N'PayloadProtected', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Payload Protected', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_checkpoint', @level2type=N'COLUMN', @level2name=N'PayloadProtected';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_checkpoint')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_checkpoint'), N'RunId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'运行标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_checkpoint', @level2type=N'COLUMN', @level2name=N'RunId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_checkpoint')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_checkpoint'), N'Sequence', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Sequence', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_checkpoint', @level2type=N'COLUMN', @level2name=N'Sequence';

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
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_event')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工智能agent event表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_event';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_event')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_event'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_event', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_event')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_event'), N'EventType', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'事件类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_event', @level2type=N'COLUMN', @level2name=N'EventType';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_event')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_event'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_event', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_event')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_event'), N'Payload', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'消息正文', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_event', @level2type=N'COLUMN', @level2name=N'Payload';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_event')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_event'), N'PayloadVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Payload Version', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_event', @level2type=N'COLUMN', @level2name=N'PayloadVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_event')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_event'), N'RunId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'运行标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_event', @level2type=N'COLUMN', @level2name=N'RunId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_event')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_event'), N'Sequence', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Sequence', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_event', @level2type=N'COLUMN', @level2name=N'Sequence';

END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_agent_event') AND name = N'IX_fn_ai_agent_event_CreatedAt')
    CREATE CLUSTERED INDEX IX_fn_ai_agent_event_CreatedAt ON dbo.fn_ai_agent_event (CreatedAtUtc, Id);
