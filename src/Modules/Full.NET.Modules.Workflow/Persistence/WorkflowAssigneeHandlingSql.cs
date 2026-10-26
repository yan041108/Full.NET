using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>系统处置记录真实节点与选人快照，不创建人工席位或假票；写入由持有实例锁的调用方完成。</summary>
internal static class WorkflowAssigneeHandlingSql
{
    public static readonly SqlStatement InsertApprovedGroup = new("workflow.assignee_handling.insert_approved_group", """
        INSERT INTO fn_workflow_approval_group
            (Id, InstanceId, StepId, GroupKey, GroupName, SequenceNo, ModeKey,
             RequiredApprovalCount, SlotCount, StatusKey, Revision, CreatedAtUtc, CompletedAtUtc)
        SELECT @Id, step.InstanceId, step.Id, @GroupKey, @GroupName, @SequenceNo, 'system',
            0, 0, 'approved', 1, @Now, @Now
        FROM fn_workflow_step AS step
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=step.InstanceId
        WHERE step.Id=@StepId AND step.InstanceId=@InstanceId AND step.NodeTypeKey='human.group_approval'
          AND step.StatusKey='active' AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active'
        """, SqlDataScope.Global);

    public static readonly SqlStatement CancelOtherWork = new("workflow.assignee_handling.cancel_other_work", """
        UPDATE fn_workflow_activity_work SET StatusKey='cancelled', CompletedAtUtc=@Now,
            LeaseOwnerKey=NULL, LeaseExpiresAtUtc=NULL
        WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey IN ('pending','leased')
          AND (@CurrentWorkId IS NULL OR Id<>@CurrentWorkId)
        """, SqlDataScope.Global);

    public static readonly SqlStatement InsertStep = new("workflow.assignee_handling.insert_step", """
        INSERT INTO fn_workflow_step
            (Id, InstanceId, NodeKey, NodeTypeKey, StatusKey, AssignedUserId, DueAtUtc,
             AttemptCount, Revision, ExecutionSequence, ParallelJoinId, ParallelBranchKey,
             StartedAtUtc, CompletedAtUtc, AssigneeSnapshotJson, AssigneeSnapshotHash)
        SELECT @Id, instance.Id, @NodeKey, @NodeTypeKey, 'completed', NULL, NULL,
            0, 1, @ExecutionSequence, @ParallelJoinId, @ParallelBranchKey,
            @Now, @Now, @AssigneeSnapshotJson, @AssigneeSnapshotHash
        FROM fn_workflow_instance AS instance
        WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active'
        """, SqlDataScope.Global);
}
