using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>依次审批沿实例锁和固定席位推进；后续席位不会提前持有待办或表单视图。</summary>
internal static class WorkflowSequentialApprovalSql
{
    public static readonly SqlStatement InsertSlot = new("workflow.sequential.insert_slot", """
        INSERT INTO fn_workflow_approval_slot
            (Id, InstanceId, StepId, TodoId, AssigneeUserId, SequenceNo, DecisionKey, Revision, CreatedAtUtc, DecidedAtUtc)
        VALUES (@Id, @InstanceId, @StepId, @TodoId, @AssigneeUserId, @SequenceNo, NULL, 1, @CreatedAtUtc, NULL)
        """, SqlDataScope.Global);

    private const string NextSlot = """
        slot.Id, slot.AssigneeUserId, slot.SequenceNo, slot.Revision
        FROM fn_workflow_approval_slot AS slot
        INNER JOIN fn_workflow_step AS step ON step.Id=slot.StepId AND step.InstanceId=slot.InstanceId
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=slot.InstanceId
        WHERE slot.InstanceId=@InstanceId AND slot.StepId=@StepId AND instance.TenantScopeKey=@TenantScopeKey
          AND instance.StatusKey='active' AND step.StatusKey='active' AND step.ApprovalModeKey='sequential'
          AND slot.TodoId IS NULL AND slot.DecisionKey IS NULL AND slot.SequenceNo IS NOT NULL
        ORDER BY slot.SequenceNo
        """;
    public static readonly SqlStatement FindNextSqlServer = new("workflow.sequential.find_next.sqlserver",
        "SELECT TOP (1) " + NextSlot, SqlDataScope.Global);
    public static readonly SqlStatement FindNextMySql = new("workflow.sequential.find_next.mysql",
        "SELECT " + NextSlot + " LIMIT 1", SqlDataScope.Global);

    public static readonly SqlStatement BindTodo = new("workflow.sequential.bind_todo", """
        UPDATE fn_workflow_approval_slot SET TodoId=@TodoId, Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND StepId=@StepId AND AssigneeUserId=@AssigneeUserId
          AND SequenceNo=@SequenceNo AND Revision=@Revision AND TodoId IS NULL AND DecisionKey IS NULL
          AND EXISTS (SELECT 1 FROM fn_workflow_todo AS todo
              WHERE todo.Id=@TodoId AND todo.InstanceId=@InstanceId AND todo.StepId=@StepId
                AND todo.AssigneeUserId=@AssigneeUserId AND todo.StatusKey='active')
          AND NOT EXISTS (SELECT 1 FROM fn_workflow_todo AS otherTodo
              WHERE otherTodo.StepId=@StepId AND otherTodo.Id<>@TodoId AND otherTodo.StatusKey='active')
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance
              INNER JOIN fn_workflow_step AS step ON step.InstanceId=instance.Id
              WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active'
                AND step.Id=@StepId AND step.StatusKey='active' AND step.ApprovalModeKey='sequential')
        """, SqlDataScope.Global);
}

/// <summary>尚未激活的固定席位，查询只能返回具有真实顺序的未决记录。</summary>
internal sealed record WorkflowPendingApprovalSlotRecord(Guid Id, Guid AssigneeUserId, int SequenceNo, long Revision);
