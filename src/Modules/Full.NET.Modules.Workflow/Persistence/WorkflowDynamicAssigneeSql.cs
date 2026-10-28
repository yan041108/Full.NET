using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>动态选人只读取当前代次明确绑定步骤的真实办理动作，不从当前组织重建历史处理人。</summary>
internal static class WorkflowDynamicAssigneeSql
{
    public static readonly SqlStatement FindClaimSnapshot = new("workflow.dynamic_assignee.find_claim_snapshot", """
        SELECT step.AssigneeSnapshotJson, step.AssigneeSnapshotHash
        FROM fn_workflow_instance AS instance
        INNER JOIN fn_workflow_claim AS pool ON pool.InstanceId=instance.Id
        INNER JOIN fn_workflow_step AS step ON step.Id=pool.Id AND step.InstanceId=instance.Id
        WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND pool.Id=@PoolId
          AND step.NodeTypeKey='human.claim'
        """, SqlDataScope.Global);
    public static readonly SqlStatement ListPreviousActors = new("workflow.dynamic_assignee.list_previous_actors", """
        SELECT step.NodeKey, step.Id AS StepId, action.ActorUserId
        FROM fn_workflow_instance AS instance
        INNER JOIN fn_workflow_activation_binding AS binding ON binding.InstanceId=instance.Id AND binding.TenantScopeKey=instance.TenantScopeKey AND binding.ExecutionEpochId=@ExecutionEpochId
        INNER JOIN fn_workflow_step AS step ON step.Id=binding.StepId AND step.InstanceId=instance.Id AND step.ExecutionEpochId=binding.SourceEpochId
        INNER JOIN fn_workflow_action_record AS action ON action.InstanceId=instance.Id AND action.StepId=step.Id
        WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND step.NodeKey IN @SourceNodeKeys
          AND step.StatusKey='completed'
          AND step.NodeTypeKey IN ('human.approval','human.group_approval','human.task','human.vote','human.claim')
          AND action.ActionKey IN ('approve','complete','vote')
        GROUP BY step.NodeKey, step.Id, action.ActorUserId
        ORDER BY step.NodeKey, action.ActorUserId
        """, SqlDataScope.Global);
}

internal sealed record WorkflowPreviousActorRecord(string NodeKey, Guid StepId, Guid ActorUserId);
internal sealed record WorkflowAssigneeSnapshotRecord(string AssigneeSnapshotJson, string AssigneeSnapshotHash);
