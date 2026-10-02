namespace Full.NET.Agents.Tools;

/// <summary>请求作用域内延迟构建工具目录，避免 DI 构造阶段同步解析远端 MCP 工具。</summary>
public interface IAgentToolRegistrySource
{
    /// <summary>
    /// 延迟构建当前请求作用域的工具目录。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>
    /// 已完成注册的 <see cref="AgentToolRegistry"/>；不应为 <see langword="null"/>，
    /// 远端 MCP 工具解析失败由实现决定抛出异常或返回空目录。
    /// </returns>
    ValueTask<AgentToolRegistry> GetRegistryAsync(CancellationToken cancellationToken = default);
}
