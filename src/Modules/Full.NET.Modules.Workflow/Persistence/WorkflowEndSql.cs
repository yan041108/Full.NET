using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>终点必须是当前固定计划选中的真实激活，绑定、因果和根终态共用调用方事务。</summary>
internal static class WorkflowEndSql
{
    public static readonly SqlStatement InsertStep = new("workflow.end.insert_step", """
        INSERT INTO fn_workflow_step (Id,InstanceId,ExecutionEpochId,NodeKey,NodeTypeKey,StatusKey,AssignedUserId,
            DueAtUtc,AttemptCount,Revision,ExecutionSequence,StartedAtUtc,CompletedAtUtc,ParallelJoinId,ParallelBranchKey)
        SELECT @Id,instance.Id,epoch.Id,@NodeKey,'end','completed',NULL,NULL,0,1,@ExecutionSequence,@Now,@Now,@ParallelJoinId,@ParallelBranchKey
        FROM fn_workflow_instance AS instance
        INNER JOIN fn_workflow_execution_epoch AS epoch ON epoch.InstanceId=instance.Id AND epoch.TenantScopeKey=instance.TenantScopeKey
        WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active'
          AND epoch.Id=@ExecutionEpochId AND epoch.StatusKey='active'
        """, SqlDataScope.Global);
}
