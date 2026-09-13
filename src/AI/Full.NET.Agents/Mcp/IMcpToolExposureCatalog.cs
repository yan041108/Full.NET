namespace Full.NET.Agents.Mcp;

/// <summary>按当前主体返回允许通过 MCP 宣告与调用的工具。</summary>
public interface IMcpToolExposureCatalog
{
    /// <summary>列出当前主体有权访问的 MCP 工具。</summary>
    ValueTask<IReadOnlyList<McpExposedToolDescriptor>> ListAuthorizedAsync(CancellationToken cancellationToken = default);

    /// <summary>查找单个已授权工具；未授权或不存在时返回 null。</summary>
    ValueTask<McpExposedToolDescriptor?> FindAuthorizedAsync(string toolName, CancellationToken cancellationToken = default);
}
