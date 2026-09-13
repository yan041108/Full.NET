using Full.NET.Agents.Tools;

namespace Full.NET.Modules.Ai.Features.ManageAgentTools;

/// <summary>按请求作用域缓存一次工具目录快照。</summary>
internal sealed class AgentToolRegistrySource(AgentToolRegistryFactory factory) : IAgentToolRegistrySource
{
    private AgentToolRegistry? registry;

    public async ValueTask<AgentToolRegistry> GetRegistryAsync(CancellationToken cancellationToken = default) =>
        registry ??= await factory.CreateAsync(cancellationToken).ConfigureAwait(false);
}
