using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>组或步骤达到固定门槛时同步关闭附属链，不能遗留可恢复原席的旧授权。</summary>
internal static class WorkflowCountersignClosureSql
{
    public static readonly SqlStatement CancelChainTodos = new("workflow.countersign_todo.cancel_by_chain", """
        UPDATE fn_workflow_todo SET StatusKey='cancelled', ResultActionKey='cancelled', CompletedAtUtc=@Now, Revision=Revision+1
        WHERE InstanceId=@InstanceId AND StepId=@StepId AND StatusKey='active'
          AND Id IN (SELECT item.TodoId FROM fn_workflow_countersign_item AS item WHERE item.ChainId=@ChainId)
          AND EXISTS (SELECT 1 FROM fn_workflow_countersign_chain AS chain
              INNER JOIN fn_workflow_instance AS instance ON instance.Id=chain.InstanceId
              WHERE chain.Id=@ChainId AND chain.InstanceId=@InstanceId AND chain.StepId=@StepId
                AND instance.TenantScopeKey=@TenantScopeKey)
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelItems = new("workflow.countersign_item.cancel_by_step", """
        UPDATE fn_workflow_countersign_item SET StatusKey='cancelled'
        WHERE StatusKey IN ('pending','active')
          AND ChainId IN (SELECT chain.Id FROM fn_workflow_countersign_chain AS chain
              WHERE chain.InstanceId=@InstanceId AND chain.StepId=@StepId AND chain.StatusKey='active'
                AND (@GroupId IS NULL OR chain.OriginTodoId IN
                    (SELECT slot.TodoId FROM fn_workflow_approval_slot AS slot
                     WHERE slot.InstanceId=@InstanceId AND slot.StepId=@StepId AND slot.GroupId=@GroupId)))
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance
              WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey)
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelChains = new("workflow.countersign_chain.cancel_by_step", """
        UPDATE fn_workflow_countersign_chain SET StatusKey='cancelled'
        WHERE InstanceId=@InstanceId AND StepId=@StepId AND StatusKey='active'
          AND (@GroupId IS NULL OR OriginTodoId IN
              (SELECT slot.TodoId FROM fn_workflow_approval_slot AS slot
               WHERE slot.InstanceId=@InstanceId AND slot.StepId=@StepId AND slot.GroupId=@GroupId))
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance
              WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey)
        """, SqlDataScope.Global);
}
