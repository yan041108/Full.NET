using Full.NET.Agents.Approvals;
using Full.NET.Modules.Ai.Features.ManageAgentApprovals;

namespace Full.NET.Modules.Ai.Features.ManageAgentTools;

/// <summary>将审批服务绑定查询适配给工具审计写入。</summary>
internal sealed class AgentToolApprovalBindingReader(AiAgentApprovalConsumption approvals) : IAgentToolApprovalBindingReader
{
    public ValueTask<AgentApprovalBinding?> FindBindingByOperationAsync(Guid operationId, CancellationToken cancellationToken) =>
        approvals.FindBindingByOperationAsync(operationId, cancellationToken);
}
