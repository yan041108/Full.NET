using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>分组事实和席位都归Workflow；组内票数与组间结果分别收敛，所有动作沿实例锁执行。</summary>
internal static class WorkflowGroupApprovalSql
{
    public static readonly SqlStatement InsertStep = new("workflow.approval_group.insert_step", """
        INSERT INTO fn_workflow_step
            (Id, InstanceId, ExecutionEpochId, NodeKey, NodeTypeKey, StatusKey, AssignedUserId, DueAtUtc, AttemptCount, Revision,
             ExecutionSequence, ApprovalModeKey, RequiredApprovalCount, ApprovalSlotCount, ApprovalGroupCount,
             ParallelJoinId, ParallelBranchKey, StartedAtUtc, CompletedAtUtc, AssigneeSnapshotJson, AssigneeSnapshotHash)
        VALUES (@Id, @InstanceId, @ExecutionEpochId, @NodeKey, 'human.group_approval', 'active', NULL, NULL, 0, 1,
            @ExecutionSequence, 'group', @RequiredApprovalCount, @ApprovalSlotCount, @ApprovalGroupCount,
            @ParallelJoinId, @ParallelBranchKey, @StartedAtUtc, NULL, @AssigneeSnapshotJson, @AssigneeSnapshotHash)
        """, SqlDataScope.Global);
    public static readonly SqlStatement InsertGroup = new("workflow.approval_group.insert", """
        INSERT INTO fn_workflow_approval_group
            (Id, InstanceId, StepId, GroupKey, GroupName, SequenceNo, ModeKey,
             RequiredApprovalCount, SlotCount, StatusKey, Revision, CreatedAtUtc, CompletedAtUtc)
        VALUES (@Id, @InstanceId, @StepId, @GroupKey, @GroupName, @SequenceNo, @ModeKey,
            @RequiredApprovalCount, @SlotCount, 'active', 1, @Now, NULL)
        """, SqlDataScope.Global);
    public static readonly SqlStatement InsertSlot = new("workflow.approval_group.insert_slot", """
        INSERT INTO fn_workflow_approval_slot
            (Id, InstanceId, StepId, GroupId, TodoId, AssigneeUserId, SequenceNo, DecisionKey, Revision, CreatedAtUtc, DecidedAtUtc)
        VALUES (@Id, @InstanceId, @StepId, @GroupId, @TodoId, @AssigneeUserId, @SequenceNo, NULL, 1, @Now, NULL)
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindForTodo = new("workflow.approval_group.find_for_todo", """
        SELECT groupState.Id, groupState.GroupKey, groupState.GroupName, groupState.ModeKey,
            groupState.RequiredApprovalCount, groupState.SlotCount, groupState.Revision, step.ApprovalGroupCount
        FROM fn_workflow_approval_slot AS slot
        INNER JOIN fn_workflow_approval_group AS groupState ON groupState.Id=slot.GroupId AND groupState.StepId=slot.StepId AND groupState.InstanceId=slot.InstanceId
        INNER JOIN fn_workflow_step AS step ON step.Id=groupState.StepId AND step.InstanceId=groupState.InstanceId
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=groupState.InstanceId
        WHERE slot.TodoId=@TodoId AND slot.StepId=@StepId AND slot.InstanceId=@InstanceId
          AND groupState.StatusKey='active' AND step.StatusKey='active' AND step.ApprovalModeKey='group'
          AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active'
        """, SqlDataScope.Global);
    /// <summary>附属签署只读取其原待办所属组；暂停或历史展示不借此取得成员一席的授权。</summary>
    public static readonly SqlStatement FindHistoryForTodo = new("workflow.approval_group.find_history_for_todo", """
        SELECT groupState.Id, groupState.GroupKey, groupState.GroupName, groupState.ModeKey,
            groupState.RequiredApprovalCount, groupState.SlotCount, groupState.Revision, step.ApprovalGroupCount
        FROM fn_workflow_approval_slot AS slot
        INNER JOIN fn_workflow_approval_group AS groupState ON groupState.Id=slot.GroupId AND groupState.StepId=slot.StepId AND groupState.InstanceId=slot.InstanceId
        INNER JOIN fn_workflow_step AS step ON step.Id=groupState.StepId AND step.InstanceId=groupState.InstanceId
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=groupState.InstanceId
        WHERE slot.TodoId=@TodoId AND slot.StepId=@StepId AND slot.InstanceId=@InstanceId
          AND instance.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    private const string GroupSlots = " FROM fn_workflow_approval_slot WHERE GroupId=@GroupId AND StepId=@StepId AND InstanceId=@InstanceId";
    public static readonly SqlStatement TallySlotsSqlServer = new("workflow.approval_group.tally_slots.sqlserver", """
        SELECT CAST(COALESCE(SUM(CASE WHEN DecisionKey='approve' THEN 1 ELSE 0 END),0) AS bigint) AS ApprovedCount,
            CAST(COALESCE(SUM(CASE WHEN DecisionKey='reject' THEN 1 ELSE 0 END),0) AS bigint) AS RejectedCount,
            CAST(COALESCE(SUM(CASE WHEN DecisionKey IS NULL THEN 1 ELSE 0 END),0) AS bigint) AS PendingCount
        """ + GroupSlots, SqlDataScope.Global);
    public static readonly SqlStatement TallySlotsMySql = new("workflow.approval_group.tally_slots.mysql", """
        SELECT CAST(COALESCE(SUM(CASE WHEN DecisionKey='approve' THEN 1 ELSE 0 END),0) AS SIGNED) AS ApprovedCount,
            CAST(COALESCE(SUM(CASE WHEN DecisionKey='reject' THEN 1 ELSE 0 END),0) AS SIGNED) AS RejectedCount,
            CAST(COALESCE(SUM(CASE WHEN DecisionKey IS NULL THEN 1 ELSE 0 END),0) AS SIGNED) AS PendingCount
        """ + GroupSlots, SqlDataScope.Global);
    private const string StepGroups = " FROM fn_workflow_approval_group WHERE StepId=@StepId AND InstanceId=@InstanceId";
    public static readonly SqlStatement TallyGroupsSqlServer = new("workflow.approval_group.tally_groups.sqlserver", """
        SELECT CAST(COALESCE(SUM(CASE WHEN StatusKey='approved' THEN 1 ELSE 0 END),0) AS bigint) AS ApprovedCount,
            CAST(COALESCE(SUM(CASE WHEN StatusKey='rejected' THEN 1 ELSE 0 END),0) AS bigint) AS RejectedCount,
            CAST(COALESCE(SUM(CASE WHEN StatusKey='active' THEN 1 ELSE 0 END),0) AS bigint) AS PendingCount
        """ + StepGroups, SqlDataScope.Global);
    public static readonly SqlStatement TallyGroupsMySql = new("workflow.approval_group.tally_groups.mysql", """
        SELECT CAST(COALESCE(SUM(CASE WHEN StatusKey='approved' THEN 1 ELSE 0 END),0) AS SIGNED) AS ApprovedCount,
            CAST(COALESCE(SUM(CASE WHEN StatusKey='rejected' THEN 1 ELSE 0 END),0) AS SIGNED) AS RejectedCount,
            CAST(COALESCE(SUM(CASE WHEN StatusKey='active' THEN 1 ELSE 0 END),0) AS SIGNED) AS PendingCount
        """ + StepGroups, SqlDataScope.Global);
    public static readonly SqlStatement CompleteGroup = new("workflow.approval_group.complete", """
        UPDATE fn_workflow_approval_group SET StatusKey=@StatusKey, CompletedAtUtc=@Now, Revision=Revision+1
        WHERE Id=@GroupId AND StepId=@StepId AND InstanceId=@InstanceId AND Revision=@Revision AND StatusKey='active'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance
              WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelGroupTodos = new("workflow.approval_group.cancel_todos", """
        UPDATE fn_workflow_todo SET StatusKey='cancelled', ResultActionKey='cancelled', CompletedAtUtc=@Now, Revision=Revision+1
        WHERE StatusKey IN ('active','before_countersign') AND InstanceId=@InstanceId AND StepId=@StepId
          AND (Id IN (SELECT TodoId FROM fn_workflow_approval_slot WHERE GroupId=@GroupId AND DecisionKey IS NULL AND TodoId IS NOT NULL)
            OR Id IN (SELECT item.TodoId FROM fn_workflow_countersign_item AS item
                INNER JOIN fn_workflow_countersign_chain AS chain ON chain.Id=item.ChainId
                INNER JOIN fn_workflow_approval_slot AS origin ON origin.TodoId=chain.OriginTodoId
                WHERE chain.InstanceId=@InstanceId AND chain.StepId=@StepId AND chain.StatusKey='active'
                  AND origin.InstanceId=@InstanceId AND origin.StepId=@StepId AND origin.GroupId=@GroupId))
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelGroupSlots = new("workflow.approval_group.cancel_slots", """
        UPDATE fn_workflow_approval_slot SET DecisionKey='cancelled', DecidedAtUtc=@Now, Revision=Revision+1
        WHERE GroupId=@GroupId AND StepId=@StepId AND InstanceId=@InstanceId AND DecisionKey IS NULL
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelActiveByStep = new("workflow.approval_group.cancel_by_step", """
        UPDATE fn_workflow_approval_group SET StatusKey='cancelled', CompletedAtUtc=@Now, Revision=Revision+1
        WHERE StepId=@StepId AND InstanceId=@InstanceId AND StatusKey='active'
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelActiveByInstance = new("workflow.approval_group.cancel_by_instance", """
        UPDATE fn_workflow_approval_group SET StatusKey='cancelled', CompletedAtUtc=@Now, Revision=Revision+1
        WHERE InstanceId=@InstanceId AND StatusKey='active'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance
              WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey)
        """, SqlDataScope.Global);
    private const string NextSlot = """
        slot.Id, slot.AssigneeUserId, slot.SequenceNo, slot.Revision, slot.GroupId
        FROM fn_workflow_approval_slot AS slot
        INNER JOIN fn_workflow_approval_group AS groupState ON groupState.Id=slot.GroupId AND groupState.StepId=slot.StepId AND groupState.InstanceId=slot.InstanceId
        INNER JOIN fn_workflow_step AS step ON step.Id=slot.StepId AND step.InstanceId=slot.InstanceId
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=slot.InstanceId
        WHERE slot.GroupId=@GroupId AND slot.StepId=@StepId AND slot.InstanceId=@InstanceId
          AND slot.TodoId IS NULL AND slot.DecisionKey IS NULL AND slot.SequenceNo IS NOT NULL
          AND groupState.ModeKey='sequential' AND groupState.StatusKey='active' AND step.StatusKey='active'
          AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active'
        ORDER BY slot.SequenceNo
        """;
    public static readonly SqlStatement FindNextSqlServer = new("workflow.approval_group.find_next.sqlserver", "SELECT TOP (1) " + NextSlot, SqlDataScope.Global);
    public static readonly SqlStatement FindNextMySql = new("workflow.approval_group.find_next.mysql", "SELECT " + NextSlot + " LIMIT 1", SqlDataScope.Global);
    // 实例锁内先独立检查活动组待办，避免MySQL在UPDATE目标席位表的子查询中再次读取自身。
    public static readonly SqlStatement CountActiveTodos = new("workflow.approval_group.count_active_todos", """
        SELECT COUNT(*) FROM fn_workflow_approval_slot AS slot
        INNER JOIN fn_workflow_todo AS todo ON todo.Id=slot.TodoId AND todo.InstanceId=slot.InstanceId AND todo.StepId=slot.StepId
        WHERE slot.GroupId=@GroupId AND slot.StepId=@StepId AND slot.InstanceId=@InstanceId AND todo.StatusKey='active'
        """, SqlDataScope.Global);
    private const string ProgressHead = "SELECT groupState.Id, groupState.GroupKey, groupState.GroupName, groupState.SequenceNo, groupState.ModeKey, groupState.RequiredApprovalCount, groupState.SlotCount, groupState.StatusKey, ";
    private const string ProgressTail = """
        FROM fn_workflow_approval_group AS groupState
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=groupState.InstanceId
        LEFT JOIN fn_workflow_approval_slot AS slot ON slot.GroupId=groupState.Id AND slot.StepId=groupState.StepId AND slot.InstanceId=groupState.InstanceId
        LEFT JOIN fn_workflow_todo_transfer AS transfer ON transfer.SlotId=slot.Id AND transfer.FromTodoId=@TodoId AND transfer.InstanceId=instance.Id
        WHERE groupState.StepId=@StepId AND groupState.InstanceId=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey
        GROUP BY groupState.Id, groupState.GroupKey, groupState.GroupName, groupState.SequenceNo, groupState.ModeKey,
            groupState.RequiredApprovalCount, groupState.SlotCount, groupState.StatusKey
        ORDER BY groupState.SequenceNo
        """;
    public static readonly SqlStatement ListProgressSqlServer = new("workflow.approval_group.list_progress.sqlserver", ProgressHead + """
        CAST(COALESCE(SUM(CASE WHEN slot.DecisionKey='approve' THEN 1 ELSE 0 END),0) AS bigint) AS ApprovedCount,
        CAST(COALESCE(SUM(CASE WHEN slot.DecisionKey='reject' THEN 1 ELSE 0 END),0) AS bigint) AS RejectedCount,
        CAST(COALESCE(SUM(CASE WHEN slot.Id IS NOT NULL AND slot.DecisionKey IS NULL THEN 1 ELSE 0 END),0) AS bigint) AS PendingCount,
        CAST(COALESCE(SUM(CASE WHEN slot.DecisionKey='cancelled' THEN 1 ELSE 0 END),0) AS bigint) AS CancelledCount,
        CAST(MAX(CASE WHEN slot.TodoId=@TodoId OR transfer.Id IS NOT NULL THEN 1 ELSE 0 END) AS bigint) AS CurrentGroup
        """ + " " + ProgressTail, SqlDataScope.Global);
    public static readonly SqlStatement ListProgressMySql = new("workflow.approval_group.list_progress.mysql", ProgressHead + """
        CAST(COALESCE(SUM(CASE WHEN slot.DecisionKey='approve' THEN 1 ELSE 0 END),0) AS SIGNED) AS ApprovedCount,
        CAST(COALESCE(SUM(CASE WHEN slot.DecisionKey='reject' THEN 1 ELSE 0 END),0) AS SIGNED) AS RejectedCount,
        CAST(COALESCE(SUM(CASE WHEN slot.Id IS NOT NULL AND slot.DecisionKey IS NULL THEN 1 ELSE 0 END),0) AS SIGNED) AS PendingCount,
        CAST(COALESCE(SUM(CASE WHEN slot.DecisionKey='cancelled' THEN 1 ELSE 0 END),0) AS SIGNED) AS CancelledCount,
        CAST(MAX(CASE WHEN slot.TodoId=@TodoId OR transfer.Id IS NOT NULL THEN 1 ELSE 0 END) AS SIGNED) AS CurrentGroup
        """ + " " + ProgressTail, SqlDataScope.Global);
    public static readonly SqlStatement BindTodo = new("workflow.approval_group.bind_todo", """
        UPDATE fn_workflow_approval_slot SET TodoId=@TodoId, Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND StepId=@StepId AND GroupId=@GroupId AND AssigneeUserId=@AssigneeUserId
          AND SequenceNo=@SequenceNo AND Revision=@Revision AND TodoId IS NULL AND DecisionKey IS NULL
          AND EXISTS (SELECT 1 FROM fn_workflow_todo AS todo
              WHERE todo.Id=@TodoId AND todo.InstanceId=@InstanceId AND todo.StepId=@StepId AND todo.AssigneeUserId=@AssigneeUserId AND todo.StatusKey='active')
          AND EXISTS (SELECT 1 FROM fn_workflow_approval_group AS groupState
              INNER JOIN fn_workflow_step AS step ON step.Id=groupState.StepId AND step.InstanceId=groupState.InstanceId
              INNER JOIN fn_workflow_instance AS instance ON instance.Id=groupState.InstanceId
              WHERE groupState.Id=@GroupId AND groupState.StepId=@StepId AND groupState.InstanceId=@InstanceId
                AND groupState.ModeKey='sequential' AND groupState.StatusKey='active' AND step.ApprovalModeKey='group' AND step.StatusKey='active'
                AND instance.StatusKey='active' AND instance.TenantScopeKey=@TenantScopeKey)
        """, SqlDataScope.Global);
}

/// <summary>从本人待办绑定的组读取组内门槛及根组数，不信任客户端指定组身份。</summary>
internal sealed record WorkflowApprovalGroupContextRecord(Guid Id, string GroupKey, string? GroupName, string ModeKey,
    int RequiredApprovalCount, int SlotCount, long Revision, int ApprovalGroupCount);
/// <summary>组内依次审批的真实未激活席位。</summary>
internal sealed record WorkflowPendingGroupApprovalSlotRecord(Guid Id, Guid AssigneeUserId, int SequenceNo, long Revision, Guid GroupId);

/// <summary>有界组进度的静态投影，原转办人识别原组不代表获得新待办权限。</summary>
internal sealed record WorkflowApprovalGroupProgressRecord(Guid Id, string GroupKey, string? GroupName, int SequenceNo,
    string ModeKey, int RequiredApprovalCount, int SlotCount, string StatusKey,
    long ApprovedCount, long RejectedCount, long PendingCount, long CancelledCount, long CurrentGroup);
