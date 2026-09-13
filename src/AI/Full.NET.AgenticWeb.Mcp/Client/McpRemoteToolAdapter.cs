using Full.NET.Agents.Mcp;
using Full.NET.Agents.Tools;
using Full.NET.AI.Abstractions.Tools;

namespace Full.NET.AgenticWeb.Mcp.Client;

/// <summary>把已批准远端能力注册为本地 Agent 工具定义。</summary>
public sealed class McpRemoteToolAdapter(
    IMcpRemoteToolCatalog catalog,
    McpClientConnectionManager connections) : IRemoteAgentToolDefinitionSource
{
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
