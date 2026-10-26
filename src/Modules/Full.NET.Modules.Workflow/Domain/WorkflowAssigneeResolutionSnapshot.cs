using System.Text.Json.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>一次真实激活的选人依据；历史事实不得用后来的组织目录重新计算。</summary>
internal sealed record WorkflowAssigneeResolutionSnapshot(int SchemaVersion, Guid InitiatorUserId,
    IReadOnlyList<Guid> MemberUserIds, IReadOnlyList<WorkflowAssigneeSourceResolution> Sources,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<WorkflowAssigneeGroupResolution>? Groups = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorkflowAssigneeHandlingDisposition? Handling = null);

/// <summary>每组保存各自的真实来源依据，不能把相同人员跨组重叠后当成两票。</summary>
internal sealed record WorkflowAssigneeGroupResolution(string GroupKey, string? GroupName, WorkflowAssigneeResolutionSnapshot Resolution);

/// <summary>来源所在配置、来源参数与本次实际输出；附加指定成员与选人策略分别标识。</summary>
internal sealed record WorkflowAssigneeSourceResolution(string OriginKey, int SourceIndex, WorkflowAssigneeSource Config,
    IReadOnlyList<Guid> UserIds, IReadOnlyList<WorkflowAssigneeMembershipBasis> Memberships);

/// <summary>实际角色、岗位、机构或主管层级的关联依据。</summary>
internal sealed record WorkflowAssigneeMembershipBasis(Guid SourceId, Guid UserId, int? Level = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? SourceRevision = null);

/// <summary>仅在一次解析调用中收集数据，失败的部分结果不进入步骤持久化。</summary>
internal sealed class WorkflowAssigneeResolutionCapture
{
    private readonly List<WorkflowAssigneeSourceResolution> sources = [];

    public void Add(int index, WorkflowAssigneeSource source, IReadOnlyList<Guid> userIds, IReadOnlyList<WorkflowAssigneeMembershipBasis> memberships) =>
        sources.Add(new("assignee_policy", index, source, userIds.ToArray(), memberships.ToArray()));

    public void AddExplicit(IReadOnlyList<Guid> userIds)
    {
        if (userIds.Count > 0) sources.Add(new("approval_policy", 0,
            new(WorkflowAssigneePolicy.SpecifiedUsers, userIds.ToArray(), [], null, null), userIds.ToArray(), []));
    }

    public void AddFallback(WorkflowAssigneeResolutionCapture attempt, string phase, int index) =>
        sources.AddRange(attempt.sources.Select(source => source with { OriginKey = $"handling.{phase}.{index}" }));

    public WorkflowAssigneeResolutionSnapshot Create(Guid initiator, IReadOnlyList<Guid> members,
        WorkflowAssigneeHandlingDisposition? handling = null) => new(1, initiator, members.ToArray(), sources.ToArray(), Handling: handling);
}
