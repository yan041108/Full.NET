using Full.NET.AI.Abstractions.Tools;
using Full.NET.Abstractions.Time;
using Full.NET.Agents.Approvals;
using Full.NET.Modules.Ai.Features.ManageAgentApprovals;

namespace Full.NET.Modules.Ai.Security;

/// <summary>写工具执行前消费审批绑定；未批准返回 Required，绑定失败返回 Denied。</summary>
internal sealed class AiAgentApprovalPort(AiAgentApprovalService approvals, IClock clock) : IAgentApprovalPort
{
    public async ValueTask<AgentApprovalExecutionStatus> ValidateForExecutionAsync(
        ToolInvocation invocation,
        string sideEffectKey,
        ToolActor actor,
        CancellationToken cancellationToken)
    {
        if (sideEffectKey is "none" or "read")
        {
            return AgentApprovalExecutionStatus.NotRequired;
        }

        var binding = await approvals.FindBindingByOperationAsync(invocation.OperationId, cancellationToken).ConfigureAwait(false);
        if (binding is null)
        {
            return AgentApprovalExecutionStatus.Required;
        }

        return AgentApprovalGate.TryValidateForConsume(
                binding,
                invocation.OperationId,
                invocation.RunId,
                invocation.ToolName ?? string.Empty,
                invocation.ToolVersion,
                invocation.Arguments,
                actor.UserId,
                actor.TenantId,
                clock.UtcNow,
                out _)
            ? AgentApprovalExecutionStatus.Approved
            : AgentApprovalExecutionStatus.Denied;
    }
}
