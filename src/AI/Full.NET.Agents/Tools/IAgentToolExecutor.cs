using Full.NET.AI.Abstractions.Tools;

namespace Full.NET.Agents.Tools;

/// <summary>统一工具执行入口；协议和模型适配器不得直接调用 Handler。</summary>
public interface IAgentToolExecutor
{
    /// <summary>完成逐次授权、预算、执行意图和结果记录。</summary>
    /// <param name="invocation">工具调用描述；租户、用户及审批由服务端注入。</param>
    /// <param name="cancellationToken">取消执行的令牌。</param>
    /// <returns>工具执行结果；Value 始终按不可信外部数据处理，失败时通过 ErrorCode 标识稳定错误码，需审批时返回 ApprovalId。</returns>
    ValueTask<ToolExecutionResult> ExecuteAsync(ToolInvocation invocation, CancellationToken cancellationToken = default);
}
