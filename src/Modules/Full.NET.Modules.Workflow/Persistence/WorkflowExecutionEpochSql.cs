using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>执行代次归属于一个真实根实例；暂停不关闭代次，重入只能创建新身份。</summary>
internal static class WorkflowExecutionEpochSql
{
    public static readonly SqlStatement InsertRoot = new("workflow.execution_epoch.insert_root", """
        INSERT INTO fn_workflow_execution_epoch (Id,InstanceId,TenantScopeKey,ParentEpochId,CauseStepId,RegionKey,
            IterationNumber,Depth,StatusKey,Revision,CreatedAtUtc,ClosedAtUtc)
        SELECT Id,Id,TenantScopeKey,NULL,NULL,'root',0,0,'active',1,@Now,NULL FROM fn_workflow_instance
        WHERE Id=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='active'
        """, SqlDataScope.Global);
    public static readonly SqlStatement Find = new("workflow.execution_epoch.find", """
        SELECT Id,InstanceId,TenantScopeKey,ParentEpochId,CauseStepId,RegionKey,IterationNumber,Depth,StatusKey,Revision,CreatedAtUtc,ClosedAtUtc
        FROM fn_workflow_execution_epoch WHERE Id=@ExecutionEpochId AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement CloseInstance = new("workflow.execution_epoch.close_instance", """
        UPDATE fn_workflow_execution_epoch SET StatusKey='closed',ClosedAtUtc=@Now,Revision=Revision+1
        WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='active'
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindForStep = new("workflow.execution_epoch.find_for_step", """
        SELECT epoch.Id FROM fn_workflow_step AS step
        INNER JOIN fn_workflow_execution_epoch AS epoch ON epoch.Id=step.ExecutionEpochId AND epoch.InstanceId=step.InstanceId
        WHERE step.Id=@StepId AND step.InstanceId=@InstanceId AND epoch.TenantScopeKey=@TenantScopeKey AND epoch.StatusKey='active'
        """, SqlDataScope.Global);
}

/// <summary>根代次复用根UUID；区域重入保存父代次及真实因果步骤，不能按同名节点复用。</summary>
internal sealed record WorkflowExecutionEpochRecord(Guid Id, Guid InstanceId, string TenantScopeKey, Guid? ParentEpochId,
    Guid? CauseStepId, string RegionKey, int IterationNumber, int Depth, string StatusKey, long Revision,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? ClosedAtUtc);
