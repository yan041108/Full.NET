using Full.NET.Agents.Tools;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentTools.Handlers;

namespace Full.NET.Modules.Ai.Features.ManageAgentTools;

/// <summary>合并本地静态工具与已批准远端 MCP 工具。</summary>
internal sealed class AgentToolRegistryFactory(
    PingToolHandler ping,
    ListModelsToolHandler listModels,
    ListChatSessionsToolHandler listSessions,
    RenameChatSessionToolHandler renameSession,
    IRemoteAgentToolDefinitionSource remoteAdapter)
{
    internal async Task<AgentToolRegistry> CreateAsync(CancellationToken cancellationToken)
    {
        var local = new AgentToolDefinition[]
        {
            new("ai.tools.ping", 1, AiAgentToolPermissions.CatalogRead, "none", true, ping),
            new("ai.models.list", 1, AiModelPermissions.Read, "read", true, listModels),
            new("ai.chat.sessions.list", 1, AiChatPermissions.Read, "read", true, listSessions),
            new("ai.chat.sessions.rename", 1, AiChatPermissions.Update, "write", true, renameSession),
        };
        var remote = await remoteAdapter.BuildDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        return new AgentToolRegistry(local.Concat(remote));
    }
}
