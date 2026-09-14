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
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工智能mcp remote connection表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_connection';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection'), N'ConnectionKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Connection Key', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_connection', @level2type=N'COLUMN', @level2name=N'ConnectionKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_connection', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection'), N'DisplayName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'显示名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_connection', @level2type=N'COLUMN', @level2name=N'DisplayName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection'), N'EndpointUrl', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Endpoint Url', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_connection', @level2type=N'COLUMN', @level2name=N'EndpointUrl';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_connection', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection'), N'IsEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_connection', @level2type=N'COLUMN', @level2name=N'IsEnabled';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection'), N'OAuthScopesJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'O Auth Scopes(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_connection', @level2type=N'COLUMN', @level2name=N'OAuthScopesJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection'), N'ScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_connection', @level2type=N'COLUMN', @level2name=N'ScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection'), N'ServiceTokenProtected', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Service Token Protected', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_connection', @level2type=N'COLUMN', @level2name=N'ServiceTokenProtected';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_connection', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_connection', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_mcp_remote_connection'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_connection', @level2type=N'COLUMN', @level2name=N'Version';
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
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工智能mcp remote tool approval表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_tool_approval';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval'), N'ApprovalStatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Approval Status Key', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_tool_approval', @level2type=N'COLUMN', @level2name=N'ApprovalStatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval'), N'ConnectionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Connection标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_tool_approval', @level2type=N'COLUMN', @level2name=N'ConnectionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_tool_approval', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_tool_approval', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval'), N'InputSchemaHash', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Input Schema Hash', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_tool_approval', @level2type=N'COLUMN', @level2name=N'InputSchemaHash';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval'), N'InputSchemaJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Input Schema(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_tool_approval', @level2type=N'COLUMN', @level2name=N'InputSchemaJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval'), N'LocalToolName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Local Tool Name', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_tool_approval', @level2type=N'COLUMN', @level2name=N'LocalToolName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval'), N'PermissionCode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'权限码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_tool_approval', @level2type=N'COLUMN', @level2name=N'PermissionCode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval'), N'RemoteToolName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Remote Tool Name', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_tool_approval', @level2type=N'COLUMN', @level2name=N'RemoteToolName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval'), N'SideEffectKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Side Effect Key', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_tool_approval', @level2type=N'COLUMN', @level2name=N'SideEffectKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval'), N'ToolVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Tool Version', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_tool_approval', @level2type=N'COLUMN', @level2name=N'ToolVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_tool_approval', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_tool_approval', @level2type=N'COLUMN', @level2name=N'Version';
    IF NOT EXISTS (
        SELECT 1 FROM sys.extended_properties
        WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval') AND minor_id = 0 AND name = N'MS_Description')
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工智能 MCP 远端工具批准表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_mcp_remote_tool_approval';
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval') AND name = N'UX_fn_ai_mcp_remote_tool_approval_Local')
    CREATE UNIQUE NONCLUSTERED INDEX UX_fn_ai_mcp_remote_tool_approval_Local ON dbo.fn_ai_mcp_remote_tool_approval (ConnectionId, LocalToolName);
