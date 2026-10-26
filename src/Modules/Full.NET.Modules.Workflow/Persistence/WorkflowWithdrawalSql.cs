using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>撤回只读取本实例实际到达边界和真实办理动作；事务内消费受信租户及发起人事实。</summary>
internal static class WorkflowWithdrawalSql
{
    public static readonly SqlStatement ListReached = new("workflow.withdrawal.list_reached", """
        SELECT DISTINCT step.NodeKey, step.NodeTypeKey, step.StatusKey
        FROM fn_workflow_step AS step
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=step.InstanceId
        WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StartedById=@ActorUserId
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountHumanActions = new("workflow.withdrawal.count_human_actions", """
        SELECT COUNT(*) FROM fn_workflow_action_record AS action
        INNER JOIN fn_workflow_step AS step ON step.Id=action.StepId AND step.InstanceId=action.InstanceId
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=action.InstanceId
        WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StartedById=@ActorUserId
          AND step.NodeTypeKey IN ('human.approval','human.group_approval','human.task','human.vote','human.claim')
          AND action.ActionKey NOT IN ('start','pause','resume','remind')
        """, SqlDataScope.Global);
}
