-- 215：MCP 远端连接与工具批准；expand-only，支持幂等重跑。
CREATE TABLE IF NOT EXISTS fn_ai_mcp_remote_connection (
    Id binary(16) NOT NULL COMMENT '逻辑主键',
    ScopeKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '可信租户 UUID 或 host',
    TenantId binary(16) NULL COMMENT '租户标识',
    ConnectionKey varchar(64) NOT NULL COMMENT '连接键',
    DisplayName varchar(128) NOT NULL COMMENT '显示名称',
    EndpointUrl varchar(512) NOT NULL COMMENT '远端端点',
    ServiceTokenProtected longtext NOT NULL COMMENT '受保护服务凭据',
    OAuthScopesJson longtext NULL COMMENT 'OAuth 作用域 JSON',
    IsEnabled tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
    Version bigint NOT NULL DEFAULT 1 COMMENT '乐观版本',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建 UTC',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新 UTC',
    PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_ai_mcp_remote_connection_Key (ScopeKey, ConnectionKey)
) COMMENT='人工智能 MCP 远端连接表';

CREATE TABLE IF NOT EXISTS fn_ai_mcp_remote_tool_approval (
    Id binary(16) NOT NULL COMMENT '逻辑主键',
    ConnectionId binary(16) NOT NULL COMMENT '所属连接',
    LocalToolName varchar(160) NOT NULL COMMENT '本地工具名',
    RemoteToolName varchar(128) NOT NULL COMMENT '远端工具名',
    ToolVersion int NOT NULL COMMENT '工具版本',
    InputSchemaJson longtext NOT NULL COMMENT '输入 Schema JSON',
    InputSchemaHash varchar(64) NOT NULL COMMENT 'Schema 摘要',
    SideEffectKey varchar(16) NOT NULL COMMENT '副作用键',
    PermissionCode varchar(128) NOT NULL COMMENT '权限码',
    ApprovalStatusKey varchar(32) NOT NULL COMMENT '批准状态',
    Version bigint NOT NULL DEFAULT 1 COMMENT '乐观版本',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建 UTC',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新 UTC',
    PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_ai_mcp_remote_tool_approval_Local (ConnectionId, LocalToolName),
    CONSTRAINT CK_fn_ai_mcp_remote_tool_approval_SideEffectKey CHECK (SideEffectKey IN ('none', 'read')),
    CONSTRAINT CK_fn_ai_mcp_remote_tool_approval_ApprovalStatusKey CHECK (ApprovalStatusKey IN ('approved', 'disabled_drift', 'pending'))
) COMMENT='人工智能 MCP 远端工具批准表';
