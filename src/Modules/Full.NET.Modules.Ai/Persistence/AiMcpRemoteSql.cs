using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>MCP 远端连接与批准 SQL。</summary>
internal static class AiMcpRemoteSql
{
    private const string ConnectionColumns = """
        c.Id,
        c.ScopeKey,
        c.TenantId,
        c.ConnectionKey,
        c.DisplayName,
        c.EndpointUrl,
        c.ServiceTokenProtected,
        c.OAuthScopesJson,
        c.IsEnabled,
        c.Version,
        c.CreatedAtUtc,
        c.UpdatedAtUtc
        """;

    public static readonly SqlStatement ListConnections = new(
        "ai.list_mcp_remote_connections",
        $"""
        SELECT {ConnectionColumns}
        FROM fn_ai_mcp_remote_connection AS c
        WHERE c.ScopeKey = @ScopeKey
          AND (@ScopeTenantId IS NULL OR c.TenantId = @ScopeTenantId)
        ORDER BY c.DisplayName
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindConnectionById = new(
        "ai.find_mcp_remote_connection_by_id",
        $"""
        SELECT {ConnectionColumns}
        FROM fn_ai_mcp_remote_connection AS c
        WHERE c.Id = @ConnectionId
          AND c.ScopeKey = @ScopeKey
          AND (@ScopeTenantId IS NULL OR c.TenantId = @ScopeTenantId)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertConnection = new(
        "ai.insert_mcp_remote_connection",
        """
        INSERT INTO fn_ai_mcp_remote_connection
            (Id, ScopeKey, TenantId, ConnectionKey, DisplayName, EndpointUrl, ServiceTokenProtected,
             OAuthScopesJson, IsEnabled, Version, CreatedAtUtc, UpdatedAtUtc)
        VALUES
            (@Id, @ScopeKey, @ScopeTenantId, @ConnectionKey, @DisplayName, @EndpointUrl, @ServiceTokenProtected,
             @OAuthScopesJson, @IsEnabled, @Version, @CreatedAtUtc, @UpdatedAtUtc)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement UpdateConnection = new(
        "ai.update_mcp_remote_connection",
        """
        UPDATE fn_ai_mcp_remote_connection
        SET DisplayName = @DisplayName,
            EndpointUrl = @EndpointUrl,
            ServiceTokenProtected = @ServiceTokenProtected,
            OAuthScopesJson = @OAuthScopesJson,
            IsEnabled = @IsEnabled,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @ConnectionId
          AND ScopeKey = @ScopeKey
          AND (@ScopeTenantId IS NULL OR TenantId = @ScopeTenantId)
          AND Version = @Version
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListApprovalsByConnection = new(
        "ai.list_mcp_remote_tool_approvals",
        """
        SELECT
            a.Id,
            a.ConnectionId,
            a.LocalToolName,
            a.RemoteToolName,
            a.ToolVersion,
            a.InputSchemaJson,
            a.InputSchemaHash,
            a.SideEffectKey,
            a.PermissionCode,
            a.ApprovalStatusKey,
            a.Version
        FROM fn_ai_mcp_remote_tool_approval AS a
        WHERE a.ConnectionId = @ConnectionId
        ORDER BY a.LocalToolName
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertApproval = new(
        "ai.insert_mcp_remote_tool_approval",
        """
        INSERT INTO fn_ai_mcp_remote_tool_approval
            (Id, ConnectionId, LocalToolName, RemoteToolName, ToolVersion, InputSchemaJson, InputSchemaHash,
             SideEffectKey, PermissionCode, ApprovalStatusKey, Version, CreatedAtUtc, UpdatedAtUtc)
        VALUES
            (@Id, @ConnectionId, @LocalToolName, @RemoteToolName, @ToolVersion, @InputSchemaJson, @InputSchemaHash,
             @SideEffectKey, @PermissionCode, @ApprovalStatusKey, @Version, @CreatedAtUtc, @UpdatedAtUtc)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement UpdateApproval = new(
        "ai.update_mcp_remote_tool_approval",
        """
        UPDATE fn_ai_mcp_remote_tool_approval
        SET RemoteToolName = @RemoteToolName,
            ToolVersion = @ToolVersion,
            InputSchemaJson = @InputSchemaJson,
            InputSchemaHash = @InputSchemaHash,
            SideEffectKey = @SideEffectKey,
            PermissionCode = @PermissionCode,
            ApprovalStatusKey = @ApprovalStatusKey,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @ApprovalId
          AND ConnectionId = @ConnectionId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindApprovalByLocalName = new(
        "ai.find_mcp_remote_tool_approval_by_local",
        """
        SELECT
            a.Id,
            a.ConnectionId,
            a.LocalToolName,
            a.RemoteToolName,
            a.ToolVersion,
            a.InputSchemaJson,
            a.InputSchemaHash,
            a.SideEffectKey,
            a.PermissionCode,
            a.ApprovalStatusKey,
            a.Version
        FROM fn_ai_mcp_remote_tool_approval AS a
        WHERE a.ConnectionId = @ConnectionId
          AND a.LocalToolName = @LocalToolName
        """,
        SqlDataScope.Global);
    internal const string ListExecutableToolsSqlServer = """
        SELECT
            c.Id AS ConnectionId,
            c.ConnectionKey,
            c.EndpointUrl,
            a.LocalToolName,
            a.RemoteToolName,
            a.ToolVersion,
            a.InputSchemaJson,
            a.InputSchemaHash,
            a.SideEffectKey,
            a.PermissionCode,
            a.ApprovalStatusKey,
            c.ServiceTokenProtected
        FROM dbo.fn_ai_mcp_remote_connection c
        INNER JOIN dbo.fn_ai_mcp_remote_tool_approval a ON a.ConnectionId = c.Id
        WHERE c.IsEnabled = 1
          AND c.ScopeKey = @ScopeKey
          AND (@ScopeTenantId IS NULL OR c.TenantId = @ScopeTenantId)
          AND a.ApprovalStatusKey = N'approved'
        """;

    internal const string ListExecutableToolsMySql = """
        SELECT
            c.Id AS ConnectionId,
            c.ConnectionKey,
            c.EndpointUrl,
            a.LocalToolName,
            a.RemoteToolName,
            a.ToolVersion,
            a.InputSchemaJson,
            a.InputSchemaHash,
            a.SideEffectKey,
            a.PermissionCode,
            a.ApprovalStatusKey,
            c.ServiceTokenProtected
        FROM fn_ai_mcp_remote_connection c
        INNER JOIN fn_ai_mcp_remote_tool_approval a ON a.ConnectionId = c.Id
        WHERE c.IsEnabled = 1
          AND c.ScopeKey = @ScopeKey
          AND (@ScopeTenantId IS NULL OR c.TenantId = @ScopeTenantId)
          AND a.ApprovalStatusKey = 'approved'
        """;
}
