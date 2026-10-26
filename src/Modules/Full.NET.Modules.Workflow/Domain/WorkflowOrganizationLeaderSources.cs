using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>把已发布的闭合来源映射为 Organization 的最小类型化只读请求，不传递租户或执行人声明。</summary>
internal static class WorkflowOrganizationLeaderSources
{
    public static WorkflowOrganizationLeaderSource? Create(WorkflowAssigneeSource source) => source.ResolverKindKey switch {
        WorkflowAssigneePolicy.OrganizationUnitLeader => new(WorkflowOrganizationLeaderSourceKind.Unit, source.UnitId),
        WorkflowAssigneePolicy.InitiatorPrimaryUnitLeader => new(WorkflowOrganizationLeaderSourceKind.InitiatorPrimaryUnit),
        WorkflowAssigneePolicy.InitiatorAncestorUnitLeader => new(WorkflowOrganizationLeaderSourceKind.InitiatorAncestorUnit, Depth: source.AncestorLevel ?? 0),
        WorkflowAssigneePolicy.InitiatorLeaderChain => new(WorkflowOrganizationLeaderSourceKind.InitiatorLeaderChain, Depth: source.AncestorLevel ?? 0),
        _ => null
    };
}
