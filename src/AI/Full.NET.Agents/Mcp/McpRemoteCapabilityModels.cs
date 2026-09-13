namespace Full.NET.Agents.Mcp;

/// <summary>管理员显式批准的远端 MCP 工具快照。</summary>
/// <param name="ConnectionId">连接标识。</param>
/// <param name="ConnectionKey">稳定连接键。</param>
/// <param name="EndpointUrl">受控 MCP HTTP 端点。</param>
/// <param name="LocalToolName">本地注册名。</param>
/// <param name="RemoteToolName">远端原始工具名。</param>
/// <param name="ToolVersion">批准版本。</param>
/// <param name="InputSchemaJson">批准时的输入 Schema JSON。</param>
/// <param name="InputSchemaHash">Schema 摘要哈希。</param>
/// <param name="SideEffectKey">本地副作用分类；不信任远端 readOnlyHint。</param>
/// <param name="PermissionCode">逐次授权权限码。</param>
/// <param name="ApprovalStatusKey">批准状态键。</param>
/// <param name="ServiceAccessToken">服务主体访问令牌；不得使用用户令牌。</param>
public sealed record McpRemoteApprovedTool(
    Guid ConnectionId,
    string ConnectionKey,
    Uri EndpointUrl,
    string LocalToolName,
    string RemoteToolName,
    int ToolVersion,
    string InputSchemaJson,
    string InputSchemaHash,
    string SideEffectKey,
    string PermissionCode,
    string ApprovalStatusKey,
    string ServiceAccessToken);

/// <summary>返回当前作用域内可执行的远端工具定义。</summary>
public interface IMcpRemoteToolCatalog
{
    /// <summary>列出已批准且可执行的远端工具。</summary>
    ValueTask<IReadOnlyList<McpRemoteApprovedTool>> ListExecutableAsync(CancellationToken cancellationToken = default);

    /// <summary>按本地工具名查找已批准远端工具。</summary>
    ValueTask<McpRemoteApprovedTool?> FindExecutableAsync(string localToolName, CancellationToken cancellationToken = default);
}
