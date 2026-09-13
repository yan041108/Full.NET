namespace Full.NET.Agents.Tools;

/// <summary>提供已批准远端 MCP 工具定义。</summary>
public interface IRemoteAgentToolDefinitionSource
{
    /// <summary>构建可注册到 <see cref="AgentToolRegistry"/> 的远端工具定义。</summary>
    ValueTask<IReadOnlyList<AgentToolDefinition>> BuildDefinitionsAsync(CancellationToken cancellationToken = default);
}
