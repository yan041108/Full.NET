namespace Full.NET.Modules.Ai.Persistence;

/// <summary>MCP 远端可执行工具行。</summary>
internal sealed class AiMcpRemoteToolRecord
{
    public Guid ConnectionId { get; init; }
    public string ConnectionKey { get; init; } = string.Empty;
    public string EndpointUrl { get; init; } = string.Empty;
    public string LocalToolName { get; init; } = string.Empty;
    public string RemoteToolName { get; init; } = string.Empty;
    public int ToolVersion { get; init; }
    public string InputSchemaJson { get; init; } = string.Empty;
    public string InputSchemaHash { get; init; } = string.Empty;
    public string SideEffectKey { get; init; } = string.Empty;
    public string PermissionCode { get; init; } = string.Empty;
    public string ApprovalStatusKey { get; init; } = string.Empty;
    public string ServiceTokenProtected { get; init; } = string.Empty;
}
