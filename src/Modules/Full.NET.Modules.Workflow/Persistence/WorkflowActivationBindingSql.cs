using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>绑定由真实激活写入；节点引用只解析为明确步骤，不能用执行序号猜测上一轮活动。</summary>
internal static class WorkflowActivationBindingSql
{
    public static readonly SqlStatement DeleteForStep = new("workflow.activation_binding.delete_for_step", """
        DELETE FROM fn_workflow_activation_binding
        WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND ExecutionEpochId=@ExecutionEpochId
          AND NodeKey IN (SELECT step.NodeKey FROM fn_workflow_step AS step
              INNER JOIN fn_workflow_instance AS instance ON instance.Id=step.InstanceId
              WHERE step.Id=@StepId AND step.InstanceId=@InstanceId AND step.ExecutionEpochId=@ExecutionEpochId AND instance.TenantScopeKey=@TenantScopeKey)
        """, SqlDataScope.Global);
    public static readonly SqlStatement Delete = new("workflow.activation_binding.delete", """
        DELETE FROM fn_workflow_activation_binding
        WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND ExecutionEpochId=@ExecutionEpochId AND NodeKey=@NodeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement Insert = new("workflow.activation_binding.insert", """
        INSERT INTO fn_workflow_activation_binding (Id,InstanceId,TenantScopeKey,ExecutionEpochId,NodeKey,StepId,SourceEpochId,CreatedAtUtc)
        SELECT @Id,step.InstanceId,instance.TenantScopeKey,epoch.Id,step.NodeKey,step.Id,step.ExecutionEpochId,@Now
        FROM fn_workflow_step AS step
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=step.InstanceId
        INNER JOIN fn_workflow_execution_epoch AS epoch ON epoch.InstanceId=instance.Id AND epoch.TenantScopeKey=instance.TenantScopeKey
        WHERE step.Id=@StepId AND step.InstanceId=@InstanceId AND step.NodeKey=@NodeKey AND step.ExecutionEpochId=@SourceEpochId
          AND instance.TenantScopeKey=@TenantScopeKey AND epoch.Id=@ExecutionEpochId AND epoch.StatusKey='active' AND instance.StatusKey='active'
          AND (step.ExecutionEpochId=epoch.Id OR step.StatusKey='completed')
        """, SqlDataScope.Global);
    public static readonly SqlStatement List = new("workflow.activation_binding.list", """
        SELECT binding.Id,binding.InstanceId,binding.TenantScopeKey,binding.ExecutionEpochId,binding.NodeKey,binding.StepId,binding.SourceEpochId,binding.CreatedAtUtc
        FROM fn_workflow_activation_binding AS binding
        INNER JOIN fn_workflow_step AS step ON step.Id=binding.StepId AND step.InstanceId=binding.InstanceId AND step.ExecutionEpochId=binding.SourceEpochId
        WHERE binding.InstanceId=@InstanceId AND binding.TenantScopeKey=@TenantScopeKey AND binding.ExecutionEpochId=@ExecutionEpochId
          AND step.StatusKey='completed'
        ORDER BY binding.NodeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement BindStep = new("workflow.activation_binding.bind_step", """
        INSERT INTO fn_workflow_activation_binding (Id,InstanceId,TenantScopeKey,ExecutionEpochId,NodeKey,StepId,SourceEpochId,CreatedAtUtc)
        SELECT step.Id,step.InstanceId,instance.TenantScopeKey,step.ExecutionEpochId,step.NodeKey,step.Id,step.ExecutionEpochId,@Now
        FROM fn_workflow_step AS step
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=step.InstanceId
        INNER JOIN fn_workflow_execution_epoch AS epoch ON epoch.Id=step.ExecutionEpochId AND epoch.InstanceId=step.InstanceId
        WHERE step.Id=@StepId AND step.InstanceId=@InstanceId AND step.ExecutionEpochId=@ExecutionEpochId
          AND instance.TenantScopeKey=@TenantScopeKey AND epoch.TenantScopeKey=@TenantScopeKey AND epoch.StatusKey='active' AND instance.StatusKey='active'
        """, SqlDataScope.Global);
}

/// <summary>代次内每个定义节点只有一个明确有效绑定；原步骤及原代次在继承后保持不变。</summary>
internal sealed record WorkflowActivationBindingRecord(Guid Id,Guid InstanceId,string TenantScopeKey,Guid ExecutionEpochId,
    string NodeKey,Guid StepId,Guid SourceEpochId,DateTimeOffset CreatedAtUtc);
