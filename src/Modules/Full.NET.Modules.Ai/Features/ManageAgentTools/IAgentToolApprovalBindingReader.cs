using Full.NET.Agents.Approvals;

namespace Full.NET.Modules.Ai.Features.ManageAgentTools;

/// <summary>工具审计读取审批绑定，不暴露审批服务完整面。</summary>
internal interface IAgentToolApprovalBindingReader
{
    ValueTask<AgentApprovalBinding?> FindBindingByOperationAsync(Guid operationId, CancellationToken cancellationToken);
}
