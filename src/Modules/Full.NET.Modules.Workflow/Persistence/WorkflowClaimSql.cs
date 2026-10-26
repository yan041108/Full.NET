using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>候选池在实例锁内唯一领取；到达不创建Todo，释放保留原始SLA和领取历史。</summary>
internal static class WorkflowClaimSql
{
    public static readonly SqlStatement InsertStep = new("workflow.claim.insert_step", """
        INSERT INTO fn_workflow_step (Id, InstanceId, NodeKey, NodeTypeKey, StatusKey, AssignedUserId,
            DueAtUtc, AttemptCount, Revision, ExecutionSequence, ParallelJoinId, ParallelBranchKey,
            StartedAtUtc, CompletedAtUtc, AssigneeSnapshotJson, AssigneeSnapshotHash)
        VALUES (@Id, @InstanceId, @NodeKey, 'human.claim', 'active', NULL,
            @DueAtUtc, 0, 1, @ExecutionSequence, @ParallelJoinId, @ParallelBranchKey, @Now, NULL, @AssigneeSnapshotJson, @AssigneeSnapshotHash)
        """, SqlDataScope.Global);
    public static readonly SqlStatement InsertPool = new("workflow.claim.insert_pool", """
        INSERT INTO fn_workflow_claim (Id, InstanceId, StatusKey, Revision, ClaimCycle, AllowRelease,
            CurrentTodoId, CurrentAssigneeUserId, DueAtUtc, NextReminderAtUtc, EscalateAtUtc, MaxReminderCount,
            ReminderIntervalMinutes, ReminderCount, EscalationRecipientUserId, EscalatedAtUtc, CreatedAtUtc, CompletedAtUtc)
        SELECT step.Id, instance.Id, 'available', 1, 0, @AllowRelease, NULL, NULL,
            @DueAtUtc, @NextReminderAtUtc, @EscalateAtUtc, @MaxReminderCount, @ReminderIntervalMinutes, 0,
            @EscalationRecipientUserId, NULL, @Now, NULL
        FROM fn_workflow_step AS step
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=step.InstanceId
        WHERE step.Id=@Id AND instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey
          AND step.NodeTypeKey='human.claim' AND step.StatusKey='active' AND instance.StatusKey='active'
        """, SqlDataScope.Global);
    public static readonly SqlStatement InsertCandidate = new("workflow.claim.insert_candidate", """
        INSERT INTO fn_workflow_claim_candidate (Id, InstanceId, PoolId, UserId, SequenceNo)
        SELECT @Id, pool.InstanceId, pool.Id, @UserId, @SequenceNo
        FROM fn_workflow_claim AS pool
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=pool.InstanceId
        WHERE pool.Id=@PoolId AND pool.InstanceId=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND pool.StatusKey='available'
        """, SqlDataScope.Global);

    private const string Projection = """
        SELECT pool.Id, pool.InstanceId, step.NodeKey, step.Revision AS StepRevision, pool.StatusKey, pool.Revision,
            pool.ClaimCycle, pool.AllowRelease, pool.CurrentTodoId, pool.CurrentAssigneeUserId, pool.CreatedAtUtc,
            pool.DueAtUtc, pool.NextReminderAtUtc, pool.EscalateAtUtc, pool.MaxReminderCount, pool.ReminderIntervalMinutes,
            pool.ReminderCount, pool.EscalationRecipientUserId, pool.EscalatedAtUtc, step.ParallelJoinId, step.ParallelBranchKey
        FROM fn_workflow_claim AS pool
        INNER JOIN fn_workflow_step AS step ON step.Id=pool.Id AND step.InstanceId=pool.InstanceId
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=pool.InstanceId
        WHERE pool.Id=@PoolId AND instance.TenantScopeKey=@TenantScopeKey
        """;
    public static readonly SqlStatement FindPool = new("workflow.claim.find_pool", Projection, SqlDataScope.Global);
    public static readonly SqlStatement ListCandidates = new("workflow.claim.list_candidates", """
        SELECT candidate.UserId, candidate.SequenceNo
        FROM fn_workflow_claim_candidate AS candidate
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=candidate.InstanceId
        WHERE candidate.PoolId=@PoolId AND candidate.InstanceId=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey
        ORDER BY candidate.SequenceNo
        """, SqlDataScope.Global);

    private const string MyPools = """
        FROM fn_workflow_claim AS pool
        INNER JOIN fn_workflow_claim_candidate AS candidate ON candidate.PoolId=pool.Id AND candidate.InstanceId=pool.InstanceId AND candidate.UserId=@ActorUserId
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=pool.InstanceId
        INNER JOIN fn_workflow_step AS step ON step.Id=pool.Id AND step.InstanceId=instance.Id
        INNER JOIN fn_workflow_definition_version AS version ON version.Id=instance.DefinitionVersionId
        INNER JOIN fn_workflow_definition AS definition ON definition.Id=version.DefinitionId
        WHERE instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active' AND pool.StatusKey='available'
        """;
    private const string MyProjection = "SELECT pool.Id, pool.InstanceId, step.NodeKey, definition.DefinitionKey, instance.BusinessType, instance.BusinessId, instance.BusinessTitle, pool.Revision, pool.ClaimCycle, pool.CreatedAtUtc, pool.DueAtUtc ";
    public static readonly SqlStatement CountMine = new("workflow.claim.count_mine", "SELECT COUNT(*) " + MyPools, SqlDataScope.Global);
    public static readonly SqlStatement ListMineSqlServer = new("workflow.claim.list_mine.sqlserver", MyProjection + MyPools + " ORDER BY pool.CreatedAtUtc, pool.Id OFFSET @Offset ROWS FETCH NEXT @Take ROWS ONLY", SqlDataScope.Global);
    public static readonly SqlStatement ListMineMySql = new("workflow.claim.list_mine.mysql", MyProjection + MyPools + " ORDER BY pool.CreatedAtUtc, pool.Id LIMIT @Take OFFSET @Offset", SqlDataScope.Global);

    public static readonly SqlStatement InsertTodo = new("workflow.claim.insert_todo", """
        INSERT INTO fn_workflow_todo (Id, InstanceId, StepId, AssigneeUserId, StatusKey, Revision, ArrivedAtUtc,
            CompletedAtUtc, ResultActionKey, DueAtUtc, NextReminderAtUtc, EscalateAtUtc, MaxReminderCount,
            ReminderIntervalMinutes, ReminderCount, EscalationRecipientUserId, EscalatedAtUtc, NextTimeoutSignalAtUtc)
        SELECT @TodoId, pool.InstanceId, pool.Id, @AssigneeUserId, 'active', 1, @Now, NULL, NULL,
            pool.DueAtUtc, pool.NextReminderAtUtc, pool.EscalateAtUtc, pool.MaxReminderCount,
            pool.ReminderIntervalMinutes, pool.ReminderCount, pool.EscalationRecipientUserId, pool.EscalatedAtUtc, @NextTimeoutSignalAtUtc
        FROM fn_workflow_claim AS pool
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=pool.InstanceId
        WHERE pool.Id=@PoolId AND instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active'
          AND pool.StatusKey='available' AND pool.Revision=@Revision AND pool.ClaimCycle<32
        """, SqlDataScope.Global);
    public static readonly SqlStatement ClaimPool = new("workflow.claim.claim_pool", """
        UPDATE fn_workflow_claim SET StatusKey='claimed', Revision=Revision+1, ClaimCycle=ClaimCycle+1,
            CurrentTodoId=@TodoId, CurrentAssigneeUserId=@AssigneeUserId
        WHERE Id=@PoolId AND InstanceId=@InstanceId AND StatusKey='available' AND Revision=@Revision AND ClaimCycle<32
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId
            AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
          AND EXISTS (SELECT 1 FROM fn_workflow_todo AS todo WHERE todo.Id=@TodoId AND todo.InstanceId=@InstanceId
            AND todo.StepId=@PoolId AND todo.AssigneeUserId=@AssigneeUserId AND todo.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement InsertAttempt = new("workflow.claim.insert_attempt", """
        INSERT INTO fn_workflow_claim_attempt (Id, InstanceId, PoolId, TodoId, AssigneeUserId, OriginalAssigneeUserId,
            ClaimedByUserId, AuthorizationKey, ClaimCycle, StatusKey, Revision, CreatedAtUtc, CompletedAtUtc)
        SELECT @Id, pool.InstanceId, pool.Id, @TodoId, @AssigneeUserId, @AssigneeUserId,
            @ActorUserId, @AuthorizationKey, pool.ClaimCycle, 'active', 1, @Now, NULL
        FROM fn_workflow_claim AS pool
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=pool.InstanceId
        WHERE pool.Id=@PoolId AND pool.InstanceId=@InstanceId AND pool.StatusKey='claimed'
          AND pool.CurrentTodoId=@TodoId AND pool.CurrentAssigneeUserId=@AssigneeUserId AND instance.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement AssignStep = new("workflow.claim.assign_step", """
        UPDATE fn_workflow_step SET AssignedUserId=@AssigneeUserId, Revision=Revision+1
        WHERE Id=@PoolId AND InstanceId=@InstanceId AND NodeTypeKey='human.claim' AND StatusKey='active' AND Revision=@StepRevision
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindAttemptForTodo = new("workflow.claim.find_attempt_for_todo", """
        SELECT attempt.Id, attempt.InstanceId, attempt.PoolId, attempt.TodoId, attempt.AssigneeUserId, attempt.OriginalAssigneeUserId,
            attempt.ClaimedByUserId, attempt.AuthorizationKey, attempt.ClaimCycle, attempt.StatusKey, attempt.Revision
        FROM fn_workflow_claim_attempt AS attempt
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=attempt.InstanceId
        WHERE attempt.PoolId=@PoolId AND attempt.InstanceId=@InstanceId AND attempt.TodoId=@TodoId AND attempt.AssigneeUserId=@ActorUserId
          AND instance.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement CompleteAttempt = new("workflow.claim.complete_attempt", """
        UPDATE fn_workflow_claim_attempt SET StatusKey=@StatusKey, CompletedAtUtc=@Now, Revision=Revision+1
        WHERE Id=@Id AND PoolId=@PoolId AND InstanceId=@InstanceId AND TodoId=@TodoId AND AssigneeUserId=@ActorUserId AND Revision=@Revision AND StatusKey='active'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CompletePool = new("workflow.claim.complete_pool", """
        UPDATE fn_workflow_claim SET StatusKey='completed', CompletedAtUtc=@Now, Revision=Revision+1
        WHERE Id=@PoolId AND InstanceId=@InstanceId AND StatusKey='claimed' AND CurrentTodoId=@TodoId AND CurrentAssigneeUserId=@ActorUserId AND Revision=@Revision
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement ReleasePool = new("workflow.claim.release_pool", """
        UPDATE fn_workflow_claim SET StatusKey='available', CurrentTodoId=NULL, CurrentAssigneeUserId=NULL, Revision=Revision+1,
            NextReminderAtUtc=@NextReminderAtUtc, ReminderCount=@ReminderCount, EscalatedAtUtc=@EscalatedAtUtc
        WHERE Id=@PoolId AND InstanceId=@InstanceId AND StatusKey='claimed' AND AllowRelease=1 AND ClaimCycle<32
          AND CurrentTodoId=@TodoId AND CurrentAssigneeUserId=@ActorUserId AND Revision=@Revision
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement ReadTodoSchedule = new("workflow.claim.read_todo_schedule", """
        SELECT todo.NextReminderAtUtc, todo.ReminderCount, todo.EscalatedAtUtc
        FROM fn_workflow_todo AS todo
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=todo.InstanceId
        WHERE todo.Id=@TodoId AND todo.InstanceId=@InstanceId AND todo.AssigneeUserId=@ActorUserId AND instance.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement RebindAttempt = new("workflow.claim.rebind_attempt", """
        UPDATE fn_workflow_claim_attempt SET TodoId=@NewTodoId, AssigneeUserId=@AssigneeUserId, Revision=Revision+1
        WHERE Id=@Id AND PoolId=@PoolId AND InstanceId=@InstanceId AND TodoId=@TodoId AND AssigneeUserId=@ActorUserId AND Revision=@Revision AND StatusKey='active'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement RebindPool = new("workflow.claim.rebind_pool", """
        UPDATE fn_workflow_claim SET CurrentTodoId=@NewTodoId, CurrentAssigneeUserId=@AssigneeUserId, Revision=Revision+1
        WHERE Id=@PoolId AND InstanceId=@InstanceId AND CurrentTodoId=@TodoId AND CurrentAssigneeUserId=@ActorUserId AND Revision=@Revision AND StatusKey='claimed'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelPools = new("workflow.claim.cancel_pools", """
        UPDATE fn_workflow_claim SET StatusKey='cancelled', Revision=Revision+1, CompletedAtUtc=@Now
        WHERE InstanceId=@InstanceId AND StatusKey IN ('available','claimed')
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey)
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelAttempts = new("workflow.claim.cancel_attempts", """
        UPDATE fn_workflow_claim_attempt SET StatusKey='cancelled', Revision=Revision+1, CompletedAtUtc=@Now
        WHERE InstanceId=@InstanceId AND StatusKey='active'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey)
        """, SqlDataScope.Global);

    public static readonly SqlStatement CountReceiptForPool = new("workflow.claim.count_receipt_for_pool", """
        SELECT COUNT(*) FROM fn_workflow_action_record AS action
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=action.InstanceId
        WHERE action.InstanceId=@InstanceId AND action.StepId=@PoolId AND action.ActorUserId=@ActorUserId
          AND action.IdempotencyKey=@IdempotencyKey AND instance.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);

    /// <summary>历史Todo按真实领取尝试或交接链取轮次，不能使用后来重新领取的池轮次。</summary>
    public static readonly SqlStatement FindHistoryCycle = new("workflow.claim.find_history_cycle", """
        SELECT attempt.ClaimCycle
        FROM fn_workflow_claim_attempt AS attempt
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=attempt.InstanceId
        INNER JOIN fn_workflow_todo AS todo ON todo.InstanceId=instance.Id AND todo.StepId=attempt.PoolId
        WHERE attempt.PoolId=@PoolId AND attempt.InstanceId=@InstanceId AND todo.Id=@TodoId
          AND todo.AssigneeUserId=@ActorUserId AND instance.TenantScopeKey=@TenantScopeKey
          AND (attempt.TodoId=todo.Id OR EXISTS (SELECT 1 FROM fn_workflow_todo_transfer AS handoff
              WHERE handoff.InstanceId=instance.Id AND handoff.ClaimAttemptId=attempt.Id
                AND (handoff.FromTodoId=todo.Id OR handoff.ToTodoId=todo.Id)))
        """, SqlDataScope.Global);
}

/// <summary>候选池的真实所有权与绝对SLA；不保存或伪造个人投票。</summary>
internal sealed record WorkflowClaimPoolRecord(Guid Id, Guid InstanceId, string NodeKey, long StepRevision, string StatusKey, long Revision,
    int ClaimCycle, bool AllowRelease, Guid? CurrentTodoId, Guid? CurrentAssigneeUserId, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? DueAtUtc, DateTimeOffset? NextReminderAtUtc, DateTimeOffset? EscalateAtUtc, int MaxReminderCount,
    int ReminderIntervalMinutes, int ReminderCount, Guid? EscalationRecipientUserId, DateTimeOffset? EscalatedAtUtc,
    Guid? ParallelJoinId, string? ParallelBranchKey);
internal sealed record WorkflowClaimCandidateRecord(Guid UserId, int SequenceNo);
internal sealed record WorkflowClaimPoolListRecord(Guid Id, Guid InstanceId, string NodeKey, string DefinitionKey, string BusinessType,
    string BusinessId, string? BusinessTitle, long Revision, int ClaimCycle, DateTimeOffset CreatedAtUtc, DateTimeOffset? DueAtUtc);
internal sealed record WorkflowClaimAttemptRecord(Guid Id, Guid InstanceId, Guid PoolId, Guid TodoId, Guid AssigneeUserId,
    Guid OriginalAssigneeUserId, Guid ClaimedByUserId, string AuthorizationKey, int ClaimCycle, string StatusKey, long Revision);
internal sealed record WorkflowClaimTodoScheduleRecord(DateTimeOffset? NextReminderAtUtc, int ReminderCount, DateTimeOffset? EscalatedAtUtc);
