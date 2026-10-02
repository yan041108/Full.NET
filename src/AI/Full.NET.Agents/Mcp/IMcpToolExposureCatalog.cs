namespace Full.NET.Agents.Mcp;

/// <summary>按当前主体返回允许通过 MCP 宣告与调用的工具。</summary>
public interface IMcpToolExposureCatalog
{
    /// <summary>列出当前主体有权访问的 MCP 工具。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前主体已授权的 MCP 工具描述符只读列表；无授权工具时返回空列表，不返回 null。</returns>
    ValueTask<IReadOnlyList<McpExposedToolDescriptor>> ListAuthorizedAsync(CancellationToken cancellationToken = default);

    /// <summary>查找单个已授权工具；未授权或不存在时返回 null。</summary>
    /// <param name="toolName">MCP 工具名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>匹配的已授权工具描述符；工具不存在或当前主体未授权时返回 null。</returns>
    ValueTask<McpExposedToolDescriptor?> FindAuthorizedAsync(string toolName, CancellationToken cancellationToken = default);
}
