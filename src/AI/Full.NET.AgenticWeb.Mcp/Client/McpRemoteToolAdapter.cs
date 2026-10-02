using Full.NET.Agents.Mcp;
using Full.NET.Agents.Tools;
using Full.NET.AI.Abstractions.Tools;

namespace Full.NET.AgenticWeb.Mcp.Client;

/// <summary>把已批准远端能力注册为本地 Agent 工具定义。</summary>
public sealed class McpRemoteToolAdapter(
    IMcpRemoteToolCatalog catalog,
    McpClientConnectionManager connections) : IRemoteAgentToolDefinitionSource
{
    /// <summary>拉取已批准的远端 MCP 能力并构造本地 AgentToolDefinition 列表；每个能力包装为 McpRemoteToolHandler 以便运行时统一调用。</summary>
    /// <param name="cancellationToken">用于取消目录查询的令牌。</param>
    /// <returns>可注册到本地工具注册表的远端工具定义集合。</returns>
    public async ValueTask<IReadOnlyList<AgentToolDefinition>> BuildDefinitionsAsync(CancellationToken cancellationToken)
    {
        var approved = await catalog.ListExecutableAsync(cancellationToken).ConfigureAwait(false);
        var definitions = new List<AgentToolDefinition>(approved.Count);
        foreach (var tool in approved)
        {
            definitions.Add(new AgentToolDefinition(
                tool.LocalToolName,
                tool.ToolVersion,
                tool.PermissionCode,
                tool.SideEffectKey,
                true,
                new McpRemoteToolHandler(tool, connections)));
        }

        return definitions;
    }
}
