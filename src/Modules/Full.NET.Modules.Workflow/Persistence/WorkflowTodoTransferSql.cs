using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>转办保留旧待办和一人一票身份，只在实例锁内建立新的真实激活。</summary>
internal static class WorkflowTodoTransferSql
{
    public static readonly SqlStatement FindLineage = new("workflow.todo_transfer.find_lineage", """
        SELECT OriginTodoId, OriginalAssigneeUserId, Depth
        FROM fn_workflow_todo_transfer
        WHERE ToTodoId=@TodoId AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);

    public static readonly SqlStatement CountPreviousActor = new("workflow.todo_transfer.count_previous_actor", """
        SELECT COUNT(*) FROM fn_workflow_todo_transfer
        WHERE OriginTodoId=@OriginTodoId AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey
          AND (FromAssigneeUserId=@AssigneeUserId OR ToAssigneeUserId=@AssigneeUserId)
        """, SqlDataScope.Global);

    public static readonly SqlStatement InsertTodo = new("workflow.todo_transfer.insert_todo", """
        INSERT INTO fn_workflow_todo
            (Id, InstanceId, StepId, AssigneeUserId, StatusKey, Revision,
             ArrivedAtUtc, CompletedAtUtc, ResultActionKey, DueAtUtc,
             NextReminderAtUtc, EscalateAtUtc, MaxReminderCount, ReminderIntervalMinutes, ReminderCount,
             EscalationRecipientUserId, EscalatedAtUtc, NextTimeoutSignalAtUtc)
        SELECT @Id, previous.InstanceId, previous.StepId, @AssigneeUserId, 'active', 1,
            @Now, NULL, NULL, previous.DueAtUtc, previous.NextReminderAtUtc, previous.EscalateAtUtc,
            previous.MaxReminderCount, previous.ReminderIntervalMinutes, previous.ReminderCount,
            previous.EscalationRecipientUserId, previous.EscalatedAtUtc, previous.NextTimeoutSignalAtUtc
        FROM fn_workflow_todo AS previous
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=previous.InstanceId
        WHERE previous.Id=@TodoId AND previous.InstanceId=@InstanceId AND previous.StepId=@StepId
          AND previous.AssigneeUserId=@ActorUserId AND previous.StatusKey='completed'
          AND previous.ResultActionKey=@FromActionKey AND instance.StatusKey='active'
          AND instance.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);

    public static readonly SqlStatement RebindSlot = new("workflow.todo_transfer.rebind_slot", """
        UPDATE fn_workflow_approval_slot
        SET TodoId=@NewTodoId, AssigneeUserId=@AssigneeUserId, Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND StepId=@StepId AND TodoId=@TodoId
          AND AssigneeUserId=@ActorUserId AND DecisionKey IS NULL AND Revision=@Revision
          AND EXISTS (SELECT 1 FROM fn_workflow_todo AS todo
              INNER JOIN fn_workflow_instance AS instance ON instance.Id=todo.InstanceId
              WHERE todo.Id=@NewTodoId AND todo.InstanceId=@InstanceId AND todo.StepId=@StepId
                AND todo.AssigneeUserId=@AssigneeUserId AND todo.StatusKey='active'
                AND instance.StatusKey='active' AND instance.TenantScopeKey=@TenantScopeKey)
        """, SqlDataScope.Global);

    public static readonly SqlStatement AdvanceStep = new("workflow.todo_transfer.advance_step", """
        UPDATE fn_workflow_step
        SET AssignedUserId=CASE WHEN AssignedUserId=@ActorUserId THEN @AssigneeUserId ELSE AssignedUserId END,
            Revision=Revision+1
        WHERE Id=@StepId AND InstanceId=@InstanceId AND Revision=@Revision AND StatusKey='active'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance
              WHERE instance.Id=@InstanceId AND instance.StatusKey='active' AND instance.TenantScopeKey=@TenantScopeKey)
        """, SqlDataScope.Global);

    public static readonly SqlStatement Insert = new("workflow.todo_transfer.insert", """
        INSERT INTO fn_workflow_todo_transfer
            (Id, InstanceId, StepId, TenantScopeKey, SlotId, TaskMemberId, VoteBallotId, ClaimAttemptId, OriginTodoId, FromTodoId, ToTodoId,
             OriginalAssigneeUserId, FromAssigneeUserId, ToAssigneeUserId, ActorUserId,
             Depth, AuthorizationKey, FormRevision, CreatedAtUtc)
        VALUES (@Id, @InstanceId, @StepId, @TenantScopeKey, @SlotId, @TaskMemberId, @VoteBallotId, @ClaimAttemptId, @OriginTodoId, @FromTodoId, @ToTodoId,
            @OriginalAssigneeUserId, @FromAssigneeUserId, @ToAssigneeUserId, @ActorUserId,
            @Depth, @AuthorizationKey, @FormRevision, @Now)
        """, SqlDataScope.Global);
}

/// <summary>由当前待办的入边读取原始身份和深度，不能由请求声明转办链。</summary>
internal sealed record WorkflowTodoTransferLineageRecord(Guid OriginTodoId, Guid OriginalAssigneeUserId, int Depth);
