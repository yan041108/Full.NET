using Full.NET.AI.Abstractions.Tools;

namespace Full.NET.Agents.Tools;

/// <summary>统一工具执行入口；协议和模型适配器不得直接调用 Handler。</summary>
public interface IAgentToolExecutor
{
    /// <summary>完成逐次授权、预算、执行意图和结果记录。</summary>
    ValueTask<ToolExecutionResult> ExecuteAsync(ToolInvocation invocation, CancellationToken cancellationToken = default);
}
