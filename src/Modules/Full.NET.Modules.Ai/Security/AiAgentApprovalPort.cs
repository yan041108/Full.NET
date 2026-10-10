using Full.NET.AI.Abstractions.Tools;
using Full.NET.Modules.Ai.Features.ManageAgentApprovals;

namespace Full.NET.Modules.Ai.Security;

/// <summary>写工具执行前只读复核审批和当前授权；消费由业务本地事务完成。</summary>
internal sealed class AiAgentApprovalPort(AiAgentApprovalConsumption approvals) : IAgentApprovalPort
{
    public ValueTask<AgentApprovalExecutionStatus> ValidateForExecutionAsync(
        ToolInvocation invocation, string sideEffectKey, ToolActor actor, CancellationToken cancellationToken) =>
        sideEffectKey is "none" or "read"
            ? ValueTask.FromResult(AgentApprovalExecutionStatus.NotRequired)
            : approvals.ValidateForToolExecutionAsync(invocation, actor, cancellationToken);
}
