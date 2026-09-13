namespace Full.NET.Modules.Ai.Contracts;

/// <summary>MCP 远端连接列表项；端点与令牌已脱敏。</summary>
public sealed record AiMcpRemoteConnectionListItem(
    Guid Id,
    string ConnectionKey,
    string DisplayName,
    string MaskedEndpointUrl,
    bool HasServiceToken,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version);

/// <summary>MCP 远端连接详情；不回显服务令牌。</summary>
public sealed record AiMcpRemoteConnectionResponse(
    Guid Id,
    string ConnectionKey,
    string DisplayName,
    string EndpointUrl,
    bool HasServiceToken,
    string? OAuthScopesJson,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version);

/// <summary>创建 MCP 远端连接请求。</summary>
public sealed record CreateAiMcpRemoteConnectionRequest(
    string ConnectionKey,
    string DisplayName,
    string EndpointUrl,
    string ServiceToken,
    string? OAuthScopesJson);

/// <summary>更新 MCP 远端连接请求。</summary>
public sealed record UpdateAiMcpRemoteConnectionRequest(
    string DisplayName,
    string EndpointUrl,
    string? ServiceToken,
    bool ClearServiceToken,
    string? OAuthScopesJson,
    bool IsEnabled,
    long Version);

/// <summary>远端 MCP 工具发现项。</summary>
public sealed record AiMcpRemoteDiscoveredToolItem(
    string RemoteToolName,
    string InputSchemaJson,
    bool IsApproved,
    string? ApprovalStatusKey);

/// <summary>批准远端 MCP 工具请求。</summary>
public sealed record ApproveAiMcpRemoteToolRequest(
    string RemoteToolName,
    string SideEffectKey,
    string PermissionCode);

/// <summary>已登记的远端工具批准项。</summary>
public sealed record AiMcpRemoteToolApprovalItem(
    Guid Id,
    string LocalToolName,
    string RemoteToolName,
    int ToolVersion,
    string SideEffectKey,
    string PermissionCode,
    string ApprovalStatusKey,
    long Version);
