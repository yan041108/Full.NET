namespace Full.NET.Modules.Ai.Persistence;

/// <summary>MCP 远端连接持久化行。</summary>
internal sealed class AiMcpRemoteConnectionRecord
{
    public Guid Id { get; init; }
    public string ScopeKey { get; init; } = string.Empty;
    public Guid? TenantId { get; init; }
    public string ConnectionKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string EndpointUrl { get; init; } = string.Empty;
    public string ServiceTokenProtected { get; init; } = string.Empty;
    public string? OAuthScopesJson { get; init; }
    public bool IsEnabled { get; init; }
    public long Version { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}

/// <summary>MCP 远端工具批准持久化行。</summary>
internal sealed class AiMcpRemoteToolApprovalRecord
{
    public Guid Id { get; init; }
    public Guid ConnectionId { get; init; }
    public string LocalToolName { get; init; } = string.Empty;
    public string RemoteToolName { get; init; } = string.Empty;
    public int ToolVersion { get; init; }
    public string InputSchemaJson { get; init; } = string.Empty;
    public string InputSchemaHash { get; init; } = string.Empty;
    public string SideEffectKey { get; init; } = string.Empty;
    public string PermissionCode { get; init; } = string.Empty;
    public string ApprovalStatusKey { get; init; } = string.Empty;
    public long Version { get; init; }
}
