using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>办理事实与审批席位独立；所有成员变化沿可信实例锁事务执行。</summary>
internal static class WorkflowTaskSql
{
    public static readonly SqlStatement InsertStep = new("workflow.task.insert_step", """
        INSERT INTO fn_workflow_step
            (Id, InstanceId, ExecutionEpochId, NodeKey, NodeTypeKey, StatusKey, AssignedUserId, DueAtUtc, AttemptCount, Revision,
             ExecutionSequence, ParallelJoinId, ParallelBranchKey, StartedAtUtc, CompletedAtUtc, AssigneeSnapshotJson, AssigneeSnapshotHash)
        VALUES (@Id, @InstanceId, @ExecutionEpochId, @NodeKey, 'human.task', 'active', @AssignedUserId, NULL, 0, 1,
            @ExecutionSequence, @ParallelJoinId, @ParallelBranchKey, @Now, NULL, @AssigneeSnapshotJson, @AssigneeSnapshotHash)
        """, SqlDataScope.Global);
    public static readonly SqlStatement InsertTask = new("workflow.task.insert", """
        INSERT INTO fn_workflow_task (Id, InstanceId, CompletionModeKey, MemberCount, RequiredCompletionCount,
            StatusKey, Revision, CreatedAtUtc, CompletedAtUtc)
        VALUES (@Id, @InstanceId, @CompletionModeKey, @MemberCount, @RequiredCompletionCount, 'active', 1, @Now, NULL)
        """, SqlDataScope.Global);
    public static readonly SqlStatement InsertMember = new("workflow.task.insert_member", """
        INSERT INTO fn_workflow_task_member (Id, InstanceId, TaskId, TodoId, AssigneeUserId, SequenceNo, StatusKey, Revision, CreatedAtUtc, CompletedAtUtc)
        VALUES (@Id, @InstanceId, @TaskId, @TodoId, @AssigneeUserId, @SequenceNo, 'pending', 1, @Now, NULL)
        """, SqlDataScope.Global);
    private const string Context = """
        SELECT member.Id, member.TaskId, member.Revision, member.SequenceNo, task.CompletionModeKey,
            task.MemberCount, task.RequiredCompletionCount, task.Revision AS TaskRevision
        FROM fn_workflow_task_member AS member
        INNER JOIN fn_workflow_task AS task ON task.Id=member.TaskId AND task.InstanceId=member.InstanceId
        INNER JOIN fn_workflow_step AS step ON step.Id=task.Id AND step.InstanceId=task.InstanceId
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=task.InstanceId
        WHERE member.TaskId=@TaskId AND member.InstanceId=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey
        """;
    public static readonly SqlStatement FindForTodo = new("workflow.task.find_for_todo", Context + """
          AND member.TodoId=@TodoId AND member.AssigneeUserId=@ActorUserId
          AND member.StatusKey='pending' AND task.StatusKey='active' AND step.StatusKey='active'
          AND step.NodeTypeKey='human.task' AND instance.StatusKey='active'
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindHistoryForTodo = new("workflow.task.find_history_for_todo", Context + """
          AND EXISTS (SELECT 1 FROM fn_workflow_todo AS original WHERE original.Id=@TodoId AND original.StepId=@TaskId
              AND original.InstanceId=@InstanceId AND original.AssigneeUserId=@ActorUserId)
          AND (member.TodoId=@TodoId OR EXISTS (SELECT 1 FROM fn_workflow_todo_transfer AS transfer
              WHERE transfer.TaskMemberId=member.Id AND transfer.FromTodoId=@TodoId AND transfer.InstanceId=@InstanceId))
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountOtherMember = new("workflow.task.count_other_member", """
        SELECT COUNT(*) FROM fn_workflow_task_member AS member
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=member.InstanceId
        WHERE member.TaskId=@TaskId AND member.InstanceId=@InstanceId AND member.Id<>@ExcludedMemberId
          AND member.AssigneeUserId=@AssigneeUserId AND instance.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement TransferMember = new("workflow.task.transfer_member", """
        UPDATE fn_workflow_task_member SET TodoId=@NewTodoId, AssigneeUserId=@AssigneeUserId, Revision=Revision+1
        WHERE Id=@Id AND TaskId=@TaskId AND InstanceId=@InstanceId AND TodoId=@TodoId AND AssigneeUserId=@ActorUserId
          AND StatusKey='pending' AND Revision=@Revision
          AND EXISTS (SELECT 1 FROM fn_workflow_todo AS todo INNER JOIN fn_workflow_instance AS instance ON instance.Id=todo.InstanceId
              WHERE todo.Id=@NewTodoId AND todo.StepId=@TaskId AND todo.InstanceId=@InstanceId AND todo.AssigneeUserId=@AssigneeUserId
                AND todo.StatusKey='active' AND instance.StatusKey='active' AND instance.TenantScopeKey=@TenantScopeKey)
        """, SqlDataScope.Global);
    private const string Members = " FROM fn_workflow_task_member WHERE TaskId=@TaskId AND InstanceId=@InstanceId";
    public static readonly SqlStatement TallySqlServer = new("workflow.task.tally.sqlserver", """
        SELECT CAST(COALESCE(SUM(CASE WHEN StatusKey='completed' THEN 1 ELSE 0 END),0) AS bigint) AS CompletedCount,
            CAST(COALESCE(SUM(CASE WHEN StatusKey='pending' THEN 1 ELSE 0 END),0) AS bigint) AS PendingCount,
            CAST(COALESCE(SUM(CASE WHEN StatusKey='cancelled' THEN 1 ELSE 0 END),0) AS bigint) AS CancelledCount
        """ + Members, SqlDataScope.Global);
    public static readonly SqlStatement TallyMySql = new("workflow.task.tally.mysql", """
        SELECT CAST(COALESCE(SUM(CASE WHEN StatusKey='completed' THEN 1 ELSE 0 END),0) AS SIGNED) AS CompletedCount,
            CAST(COALESCE(SUM(CASE WHEN StatusKey='pending' THEN 1 ELSE 0 END),0) AS SIGNED) AS PendingCount,
            CAST(COALESCE(SUM(CASE WHEN StatusKey='cancelled' THEN 1 ELSE 0 END),0) AS SIGNED) AS CancelledCount
        """ + Members, SqlDataScope.Global);
    public static readonly SqlStatement CompleteMember = new("workflow.task.complete_member", """
        UPDATE fn_workflow_task_member SET StatusKey='completed', CompletedAtUtc=@Now, Revision=Revision+1
        WHERE Id=@Id AND TaskId=@TaskId AND InstanceId=@InstanceId AND TodoId=@TodoId AND AssigneeUserId=@ActorUserId
          AND StatusKey='pending' AND Revision=@Revision
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId
              AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CompleteTask = new("workflow.task.complete", """
        UPDATE fn_workflow_task SET StatusKey='completed', CompletedAtUtc=@Now, Revision=Revision+1
        WHERE Id=@TaskId AND InstanceId=@InstanceId AND Revision=@Revision AND StatusKey='active'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId
              AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelMemberTodos = new("workflow.task.cancel_member_todos", """
        UPDATE fn_workflow_todo SET StatusKey='cancelled', ResultActionKey='cancelled', CompletedAtUtc=@Now, Revision=Revision+1
        WHERE StepId=@TaskId AND InstanceId=@InstanceId AND StatusKey='active'
          AND Id IN (SELECT TodoId FROM fn_workflow_task_member WHERE TaskId=@TaskId AND InstanceId=@InstanceId AND StatusKey='pending' AND TodoId IS NOT NULL)
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelMembers = new("workflow.task.cancel_members", """
        UPDATE fn_workflow_task_member SET StatusKey='cancelled', CompletedAtUtc=@Now, Revision=Revision+1
        WHERE TaskId=@TaskId AND InstanceId=@InstanceId AND StatusKey='pending'
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelByStep = new("workflow.task.cancel_by_step", """
        UPDATE fn_workflow_task SET StatusKey='cancelled', CompletedAtUtc=@Now, Revision=Revision+1
        WHERE Id=@TaskId AND InstanceId=@InstanceId AND StatusKey='active'
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelByInstance = new("workflow.task.cancel_by_instance", """
        UPDATE fn_workflow_task SET StatusKey='cancelled', CompletedAtUtc=@Now, Revision=Revision+1
        WHERE InstanceId=@InstanceId AND StatusKey='active'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey)
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelMembersByInstance = new("workflow.task.cancel_members_by_instance", """
        UPDATE fn_workflow_task_member SET StatusKey='cancelled', CompletedAtUtc=@Now, Revision=Revision+1
        WHERE InstanceId=@InstanceId AND StatusKey='pending'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey)
        """, SqlDataScope.Global);
    private const string Next = """
        member.Id, member.AssigneeUserId, member.SequenceNo, member.Revision
        FROM fn_workflow_task_member AS member
        INNER JOIN fn_workflow_task AS task ON task.Id=member.TaskId AND task.InstanceId=member.InstanceId
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=task.InstanceId
        WHERE member.TaskId=@TaskId AND member.InstanceId=@InstanceId AND member.TodoId IS NULL AND member.StatusKey='pending'
          AND task.StatusKey='active' AND task.CompletionModeKey='sequential' AND instance.StatusKey='active'
          AND instance.TenantScopeKey=@TenantScopeKey ORDER BY member.SequenceNo
        """;
    public static readonly SqlStatement FindNextSqlServer = new("workflow.task.find_next.sqlserver", "SELECT TOP (1) " + Next, SqlDataScope.Global);
    public static readonly SqlStatement FindNextMySql = new("workflow.task.find_next.mysql", "SELECT " + Next + " LIMIT 1", SqlDataScope.Global);
    public static readonly SqlStatement CountActiveTodos = new("workflow.task.count_active_todos", """
        SELECT COUNT(*) FROM fn_workflow_task_member AS member
        INNER JOIN fn_workflow_todo AS todo ON todo.Id=member.TodoId AND todo.StepId=member.TaskId AND todo.InstanceId=member.InstanceId
        WHERE member.TaskId=@TaskId AND member.InstanceId=@InstanceId AND todo.StatusKey='active'
        """, SqlDataScope.Global);
    public static readonly SqlStatement BindTodo = new("workflow.task.bind_todo", """
        UPDATE fn_workflow_task_member SET TodoId=@TodoId, Revision=Revision+1
        WHERE Id=@Id AND TaskId=@TaskId AND InstanceId=@InstanceId AND AssigneeUserId=@AssigneeUserId
          AND SequenceNo=@SequenceNo AND Revision=@Revision AND StatusKey='pending' AND TodoId IS NULL
          AND EXISTS (SELECT 1 FROM fn_workflow_todo AS todo WHERE todo.Id=@TodoId AND todo.StepId=@TaskId
              AND todo.InstanceId=@InstanceId AND todo.AssigneeUserId=@AssigneeUserId AND todo.StatusKey='active')
          AND EXISTS (SELECT 1 FROM fn_workflow_task AS task INNER JOIN fn_workflow_instance AS instance ON instance.Id=task.InstanceId
              WHERE task.Id=@TaskId AND task.InstanceId=@InstanceId AND task.StatusKey='active'
                AND task.CompletionModeKey='sequential' AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
}

/// <summary>当前本人待办的固定办理成员及活动门槛。</summary>
internal sealed record WorkflowTaskContextRecord(Guid Id, Guid TaskId, long Revision, int SequenceNo,
    string CompletionModeKey, int MemberCount, int RequiredCompletionCount, long TaskRevision);
/// <summary>独立办理提交计数，不含同意票或反对票。</summary>
internal sealed record WorkflowTaskTallyRecord(long CompletedCount, long PendingCount, long CancelledCount);
/// <summary>顺序办理的下一未激活成员。</summary>
internal sealed record WorkflowPendingTaskMemberRecord(Guid Id, Guid AssigneeUserId, int SequenceNo, long Revision);
