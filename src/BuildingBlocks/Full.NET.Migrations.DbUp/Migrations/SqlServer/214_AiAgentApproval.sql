-- 214：Agent 审批、委托与工具审计扩展；expand-only，支持幂等重跑。
IF OBJECT_ID(N'dbo.fn_ai_agent_approval', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_agent_approval (
        Id uniqueidentifier NOT NULL,
        ScopeKey varchar(32) NOT NULL,
        TenantId uniqueidentifier NULL,
        RunId uniqueidentifier NOT NULL,
        OperationId uniqueidentifier NOT NULL,
        SessionId uniqueidentifier NOT NULL,
        ToolName varchar(128) NOT NULL,
        ToolVersion int NOT NULL,
        ArgumentsHash varchar(64) NOT NULL,
        ArgumentsProtected nvarchar(max) NOT NULL,
        PolicyVersion int NOT NULL,
        PresentationJson nvarchar(max) NOT NULL,
        RequestedBy uniqueidentifier NOT NULL,
        ApproverId uniqueidentifier NULL,
        DecisionKey varchar(16) NOT NULL,
        ExpiresAtUtc datetimeoffset(7) NOT NULL,
        ConsumedAtUtc datetimeoffset(7) NULL,
        Version bigint NOT NULL CONSTRAINT DF_fn_ai_agent_approval_Version DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_ai_agent_approval PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_ai_agent_approval_DecisionKey CHECK (DecisionKey IN (N'pending', N'approved', N'denied'))
    );
    IF NOT EXISTS (
        SELECT 1 FROM sys.extended_properties
        WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_approval') AND minor_id = 0 AND name = N'MS_Description')
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工智能 Agent 审批表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_approval';
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_agent_approval') AND name = N'UX_fn_ai_agent_approval_Operation')
    CREATE UNIQUE NONCLUSTERED INDEX UX_fn_ai_agent_approval_Operation ON dbo.fn_ai_agent_approval (OperationId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_agent_approval') AND name = N'IX_fn_ai_agent_approval_Run')
    CREATE NONCLUSTERED INDEX IX_fn_ai_agent_approval_Run ON dbo.fn_ai_agent_approval (RunId, CreatedAtUtc);

IF OBJECT_ID(N'dbo.fn_ai_agent_delegation', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_agent_delegation (
        Id uniqueidentifier NOT NULL,
        ScopeKey varchar(32) NOT NULL,
        TenantId uniqueidentifier NULL,
        GrantorUserId uniqueidentifier NOT NULL,
        GranteeUserId uniqueidentifier NOT NULL,
        ToolName varchar(128) NULL,
        PermissionCode varchar(128) NULL,
        ExpiresAtUtc datetimeoffset(7) NOT NULL,
        RevokedAtUtc datetimeoffset(7) NULL,
        Version bigint NOT NULL CONSTRAINT DF_fn_ai_agent_delegation_Version DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_ai_agent_delegation PRIMARY KEY NONCLUSTERED (Id)
    );
    IF NOT EXISTS (
        SELECT 1 FROM sys.extended_properties
        WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_delegation') AND minor_id = 0 AND name = N'MS_Description')
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工智能 Agent 委托表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_delegation';
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_agent_delegation') AND name = N'IX_fn_ai_agent_delegation_Grantee')
    CREATE NONCLUSTERED INDEX IX_fn_ai_agent_delegation_Grantee ON dbo.fn_ai_agent_delegation (GranteeUserId, ExpiresAtUtc);

IF COL_LENGTH(N'dbo.fn_ai_agent_tool_call', N'RunId') IS NULL
    ALTER TABLE dbo.fn_ai_agent_tool_call ADD RunId uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.fn_ai_agent_tool_call', N'StepId') IS NULL
    ALTER TABLE dbo.fn_ai_agent_tool_call ADD StepId uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.fn_ai_agent_tool_call', N'ArgumentsHash') IS NULL
    ALTER TABLE dbo.fn_ai_agent_tool_call ADD ArgumentsHash varchar(64) NULL;
IF COL_LENGTH(N'dbo.fn_ai_agent_tool_call', N'ApprovalId') IS NULL
    ALTER TABLE dbo.fn_ai_agent_tool_call ADD ApprovalId uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.fn_ai_agent_tool_call', N'ReceiptId') IS NULL
    ALTER TABLE dbo.fn_ai_agent_tool_call ADD ReceiptId uniqueidentifier NULL;
