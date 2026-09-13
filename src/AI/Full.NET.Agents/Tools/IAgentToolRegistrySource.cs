namespace Full.NET.Agents.Tools;

/// <summary>请求作用域内延迟构建工具目录，避免 DI 构造阶段同步解析远端 MCP 工具。</summary>
public interface IAgentToolRegistrySource
{
    ValueTask<AgentToolRegistry> GetRegistryAsync(CancellationToken cancellationToken = default);
}
