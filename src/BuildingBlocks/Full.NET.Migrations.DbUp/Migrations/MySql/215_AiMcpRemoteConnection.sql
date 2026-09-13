-- 215：MCP 远端连接与工具批准；expand-only，支持幂等重跑。
CREATE TABLE IF NOT EXISTS fn_ai_mcp_remote_connection (
    Id char(36) NOT NULL,
    ScopeKey varchar(32) NOT NULL,
    TenantId char(36) NULL,
    ConnectionKey varchar(64) NOT NULL,
    DisplayName varchar(128) NOT NULL,
    EndpointUrl varchar(512) NOT NULL,
    ServiceTokenProtected longtext NOT NULL,
    OAuthScopesJson longtext NULL,
    IsEnabled tinyint(1) NOT NULL DEFAULT 1,
    Version bigint NOT NULL DEFAULT 1,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NOT NULL,
    PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_ai_mcp_remote_connection_Key (ScopeKey, ConnectionKey)
) COMMENT='人工智能 MCP 远端连接表';

CREATE TABLE IF NOT EXISTS fn_ai_mcp_remote_tool_approval (
    Id char(36) NOT NULL,
    ConnectionId char(36) NOT NULL,
    LocalToolName varchar(160) NOT NULL,
    RemoteToolName varchar(128) NOT NULL,
    ToolVersion int NOT NULL,
    InputSchemaJson longtext NOT NULL,
    InputSchemaHash varchar(64) NOT NULL,
    SideEffectKey varchar(16) NOT NULL,
    PermissionCode varchar(128) NOT NULL,
    ApprovalStatusKey varchar(32) NOT NULL,
    Version bigint NOT NULL DEFAULT 1,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NOT NULL,
    PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_ai_mcp_remote_tool_approval_Local (ConnectionId, LocalToolName),
    CONSTRAINT CK_fn_ai_mcp_remote_tool_approval_SideEffectKey CHECK (SideEffectKey IN ('none', 'read')),
    CONSTRAINT CK_fn_ai_mcp_remote_tool_approval_ApprovalStatusKey CHECK (ApprovalStatusKey IN ('approved', 'disabled_drift', 'pending'))
) COMMENT='人工智能 MCP 远端工具批准表';
