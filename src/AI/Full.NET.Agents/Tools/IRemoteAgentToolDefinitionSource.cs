namespace Full.NET.Agents.Tools;

/// <summary>提供已批准远端 MCP 工具定义。</summary>
public interface IRemoteAgentToolDefinitionSource
{
    /// <summary>构建可注册到 <see cref="AgentToolRegistry"/> 的远端工具定义。</summary>
    /// <param name="cancellationToken">用于取消远端工具解析的令牌。</param>
    /// <returns>
    /// 已批准远端 MCP 工具的只读定义集合；无可用工具时返回空集合而非 <see langword="null"/>。
    /// </returns>
    ValueTask<IReadOnlyList<AgentToolDefinition>> BuildDefinitionsAsync(CancellationToken cancellationToken = default);
}
