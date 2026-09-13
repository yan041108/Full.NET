-- 215：MCP 远端连接与工具批准；expand-only，支持幂等重跑。
IF OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_mcp_remote_connection (
        Id uniqueidentifier NOT NULL,
        ScopeKey varchar(32) NOT NULL,
        TenantId uniqueidentifier NULL,
        ConnectionKey varchar(64) NOT NULL,
        DisplayName nvarchar(128) NOT NULL,
        EndpointUrl nvarchar(512) NOT NULL,
        ServiceTokenProtected nvarchar(max) NOT NULL,
        OAuthScopesJson nvarchar(max) NULL,
        IsEnabled bit NOT NULL CONSTRAINT DF_fn_ai_mcp_remote_connection_IsEnabled DEFAULT (1),
        Version bigint NOT NULL CONSTRAINT DF_fn_ai_mcp_remote_connection_Version DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_ai_mcp_remote_connection PRIMARY KEY NONCLUSTERED (Id)
    );
    IF NOT EXISTS (
        SELECT 1 FROM sys.extended_properties
        WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection') AND minor_id = 0 AND name = N'MS_Description')
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工智能 MCP 远端连接表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_connection';
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection') AND name = N'UX_fn_ai_mcp_remote_connection_Key')
    CREATE UNIQUE NONCLUSTERED INDEX UX_fn_ai_mcp_remote_connection_Key ON dbo.fn_ai_mcp_remote_connection (ScopeKey, ConnectionKey);

IF OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_mcp_remote_tool_approval (
        Id uniqueidentifier NOT NULL,
        ConnectionId uniqueidentifier NOT NULL,
        LocalToolName varchar(160) NOT NULL,
        RemoteToolName varchar(128) NOT NULL,
        ToolVersion int NOT NULL,
        InputSchemaJson nvarchar(max) NOT NULL,
        InputSchemaHash varchar(64) NOT NULL,
        SideEffectKey varchar(16) NOT NULL,
        PermissionCode varchar(128) NOT NULL,
        ApprovalStatusKey varchar(32) NOT NULL,
        Version bigint NOT NULL CONSTRAINT DF_fn_ai_mcp_remote_tool_approval_Version DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_ai_mcp_remote_tool_approval PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_ai_mcp_remote_tool_approval_SideEffectKey CHECK (SideEffectKey IN (N'none', N'read')),
        CONSTRAINT CK_fn_ai_mcp_remote_tool_approval_ApprovalStatusKey CHECK (ApprovalStatusKey IN (N'approved', N'disabled_drift', N'pending'))
    );
    IF NOT EXISTS (
        SELECT 1 FROM sys.extended_properties
        WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval') AND minor_id = 0 AND name = N'MS_Description')
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工智能 MCP 远端工具批准表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_tool_approval';
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval') AND name = N'UX_fn_ai_mcp_remote_tool_approval_Local')
    CREATE UNIQUE NONCLUSTERED INDEX UX_fn_ai_mcp_remote_tool_approval_Local ON dbo.fn_ai_mcp_remote_tool_approval (ConnectionId, LocalToolName);
