using Full.NET.Agents.Tools;

namespace Full.NET.Modules.Ai.Features.ManageAgentTools;

/// <summary>每次构建当前工具目录；审批及审计等待后不能复用撤回前的远端策略快照。</summary>
internal sealed class AgentToolRegistrySource(AgentToolRegistryFactory factory) : IAgentToolRegistrySource
{
    public async ValueTask<AgentToolRegistry> GetRegistryAsync(CancellationToken cancellationToken = default) =>
        await factory.CreateAsync(cancellationToken).ConfigureAwait(false);
}
