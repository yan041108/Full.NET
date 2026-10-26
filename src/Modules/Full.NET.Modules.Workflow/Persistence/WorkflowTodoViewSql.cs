using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>人工输入输出属于本次待办，不借用其他办理人或未来节点的表单。</summary>
internal static class WorkflowTodoViewSql
{
    public static readonly SqlStatement Capture = new("workflow.todo_view.capture", """
        INSERT INTO fn_workflow_todo_view (Id, InstanceId, TenantScopeKey, InputJson, InputRevision)
        SELECT todo.Id, todo.InstanceId, instance.TenantScopeKey, @InputJson,
            CASE WHEN step.ParallelJoinId IS NULL THEN submission.Revision ELSE branch.Revision END
        FROM fn_workflow_todo AS todo
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=todo.InstanceId
        INNER JOIN fn_workflow_step AS step ON step.Id=todo.StepId
        INNER JOIN fn_workflow_form_submission AS submission ON submission.InstanceId=instance.Id
        LEFT JOIN fn_workflow_branch_state AS branch ON branch.ParallelJoinId=step.ParallelJoinId AND branch.BranchKey=step.ParallelBranchKey
        WHERE todo.Id=@Id AND instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey
          AND (step.ParallelJoinId IS NULL OR branch.BranchKey IS NOT NULL)
        """, SqlDataScope.Global);

    /// <summary>退回与加签当前仅支持主路径，在新待办实际激活时取事务内的完整输入。</summary>
    public static readonly SqlStatement CaptureCurrentMain = new("workflow.todo_view.capture_current_main", """
        INSERT INTO fn_workflow_todo_view (Id, InstanceId, TenantScopeKey, InputJson, InputRevision)
        SELECT todo.Id, todo.InstanceId, instance.TenantScopeKey, submission.SubmissionJson, submission.Revision
        FROM fn_workflow_todo AS todo
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=todo.InstanceId
        INNER JOIN fn_workflow_step AS step ON step.Id=todo.StepId
        INNER JOIN fn_workflow_form_submission AS submission ON submission.InstanceId=instance.Id
        WHERE todo.Id=@Id AND instance.TenantScopeKey=@TenantScopeKey AND step.ParallelJoinId IS NULL
        """, SqlDataScope.Global);

    public static readonly SqlStatement SetOutput = new("workflow.todo_view.set_output", """
        UPDATE fn_workflow_todo_view SET OutputJson=@OutputJson, OutputRevision=@OutputRevision
        WHERE Id=@TodoId AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND OutputRevision IS NULL
          AND InputRevision<@OutputRevision
        """, SqlDataScope.Global);

    public static readonly SqlStatement FindOwned = new("workflow.todo_view.find_owned", """
        SELECT viewRecord.InputJson, viewRecord.InputRevision, viewRecord.OutputJson, viewRecord.OutputRevision
        FROM fn_workflow_todo_view AS viewRecord
        INNER JOIN fn_workflow_todo AS todo ON todo.Id=viewRecord.Id AND todo.InstanceId=viewRecord.InstanceId
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=viewRecord.InstanceId AND instance.TenantScopeKey=viewRecord.TenantScopeKey
        WHERE todo.Id=@TodoId AND todo.AssigneeUserId=@ActorUserId AND viewRecord.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
}

/// <summary>人工活动不可变输入与本人办理输出的固定读取投影。</summary>
internal sealed record WorkflowTodoViewRecord(string InputJson, long InputRevision, string? OutputJson, long? OutputRevision);
