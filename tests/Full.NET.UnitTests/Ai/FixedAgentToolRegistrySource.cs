using Full.NET.Agents.Tools;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试用固定工具目录源，避免构造 AgentToolExecutor 时同步解析 DI。</summary>
internal sealed class FixedAgentToolRegistrySource(AgentToolRegistry registry) : IAgentToolRegistrySource
{
    public ValueTask<AgentToolRegistry> GetRegistryAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(registry);
}