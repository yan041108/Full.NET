using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>独立选票、个人视图与汇总均沿可信实例锁写入，个人表单不能覆盖共享主表单。</summary>
internal static class WorkflowVoteSql
{
    /// <summary>零有效票的挂起必须显式处置结果，通用恢复不能把它当成可重试活动。</summary>
    public static readonly SqlStatement CountUnresolved = new("workflow.vote.count_unresolved", """
        SELECT COUNT(*) FROM fn_workflow_vote AS vote
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=vote.InstanceId
        WHERE vote.InstanceId=@InstanceId AND vote.StatusKey='suspended' AND instance.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    private const string DeadlineMember = """
        FROM fn_workflow_vote_ballot AS ballot
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=ballot.InstanceId
        WHERE ballot.VoteId=@VoteId AND ballot.InstanceId=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey
        ORDER BY ballot.Id
        """;
    public static readonly SqlStatement FindDeadlineMemberSqlServer = new("workflow.vote.find_deadline_member.sqlserver",
        "SELECT TOP (1) ballot.TodoId, ballot.AssigneeUserId " + DeadlineMember, SqlDataScope.Global);
    public static readonly SqlStatement FindDeadlineMemberMySql = new("workflow.vote.find_deadline_member.mysql",
        "SELECT ballot.TodoId, ballot.AssigneeUserId " + DeadlineMember + " LIMIT 1", SqlDataScope.Global);
    public static readonly SqlStatement FindFormCandidates = new("workflow.vote.find_form_candidates", """
        SELECT version.Id, version.FormSchemaJson, definition.StatusKey AS FormStatusKey
        FROM fn_workflow_form_version AS version
        INNER JOIN fn_workflow_form_definition AS definition ON definition.Id=version.FormDefinitionId
        WHERE version.Id IN @FormVersionIds AND definition.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement InsertFormBinding = new("workflow.vote.insert_form_binding", """
        INSERT INTO fn_workflow_vote_form (Id, DefinitionVersionId, NodeKey, BallotFormVersionId, FormSchemaHash)
        SELECT @Id, version.Id, @NodeKey, formVersion.Id, @FormSchemaHash
        FROM fn_workflow_definition_version AS version
        INNER JOIN fn_workflow_definition AS definition ON definition.Id=version.DefinitionId
        INNER JOIN fn_workflow_form_version AS formVersion ON formVersion.Id=@BallotFormVersionId
        INNER JOIN fn_workflow_form_definition AS formDefinition ON formDefinition.Id=formVersion.FormDefinitionId
        WHERE version.Id=@DefinitionVersionId AND definition.TenantScopeKey=@TenantScopeKey
          AND formDefinition.TenantScopeKey=@TenantScopeKey AND formDefinition.StatusKey='active'
        """, SqlDataScope.Global);

    public static readonly SqlStatement FindFormBinding = new("workflow.vote.find_form_binding", """
        SELECT binding.Id, binding.BallotFormVersionId, formVersion.FormSchemaJson, binding.FormSchemaHash, formDefinition.StatusKey AS FormStatusKey
        FROM fn_workflow_vote_form AS binding
        INNER JOIN fn_workflow_definition_version AS version ON version.Id=binding.DefinitionVersionId
        INNER JOIN fn_workflow_definition AS definition ON definition.Id=version.DefinitionId
        INNER JOIN fn_workflow_form_version AS formVersion ON formVersion.Id=binding.BallotFormVersionId
        INNER JOIN fn_workflow_form_definition AS formDefinition ON formDefinition.Id=formVersion.FormDefinitionId
        WHERE binding.DefinitionVersionId=@DefinitionVersionId AND binding.NodeKey=@NodeKey
          AND definition.TenantScopeKey=@TenantScopeKey AND formDefinition.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);

    public static readonly SqlStatement CountFormVersionReferences = new("workflow.vote.count_form_version_references", """
        SELECT COUNT(*) FROM fn_workflow_vote_form AS binding
        INNER JOIN fn_workflow_form_version AS version ON version.Id=binding.BallotFormVersionId
        INNER JOIN fn_workflow_form_definition AS definition ON definition.Id=version.FormDefinitionId
        WHERE binding.BallotFormVersionId=@FormVersionId AND definition.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);

    public static readonly SqlStatement InsertStep = new("workflow.vote.insert_step", """
        INSERT INTO fn_workflow_step (Id, InstanceId, ExecutionEpochId, NodeKey, NodeTypeKey, StatusKey, AssignedUserId,
            DueAtUtc, AttemptCount, Revision, ExecutionSequence, ParallelJoinId, ParallelBranchKey,
            StartedAtUtc, CompletedAtUtc, AssigneeSnapshotJson, AssigneeSnapshotHash)
        VALUES (@Id, @InstanceId, @ExecutionEpochId, @NodeKey, 'human.vote', 'active', NULL,
            @ClosesAtUtc, 0, 1, @ExecutionSequence, @ParallelJoinId, @ParallelBranchKey,
            @Now, NULL, @AssigneeSnapshotJson, @AssigneeSnapshotHash)
        """, SqlDataScope.Global);

    public static readonly SqlStatement InsertRound = new("workflow.vote.insert_round", """
        INSERT INTO fn_workflow_vote (Id, InstanceId, FormBindingId, MemberCount, ParticipationPercentage,
            ApprovalPercentage, AllowAbstain, ClosesAtUtc, StatusKey, Revision, CreatedAtUtc, CompletedAtUtc,
            FinalYesCount, FinalNoCount, FinalAbstainCount, FinalUnsubmittedCount)
        SELECT step.Id, instance.Id, binding.Id, @MemberCount, @ParticipationPercentage,
            @ApprovalPercentage, @AllowAbstain, @ClosesAtUtc, 'active', 1, @Now, NULL, NULL, NULL, NULL, NULL
        FROM fn_workflow_step AS step
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=step.InstanceId
        INNER JOIN fn_workflow_vote_form AS binding ON binding.DefinitionVersionId=instance.DefinitionVersionId AND binding.NodeKey=step.NodeKey
        WHERE step.Id=@Id AND instance.Id=@InstanceId AND binding.Id=@FormBindingId
          AND step.NodeTypeKey='human.vote' AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active'
        """, SqlDataScope.Global);

    public static readonly SqlStatement InsertBallot = new("workflow.vote.insert_ballot", """
        INSERT INTO fn_workflow_vote_ballot (Id, InstanceId, VoteId, TodoId, OriginalAssigneeUserId,
            AssigneeUserId, StatusKey, ChoiceKey, InputJson, SubmissionJson, Revision, CreatedAtUtc, SubmittedAtUtc, CancelledAtUtc)
        SELECT @Id, vote.InstanceId, vote.Id, todo.Id, @AssigneeUserId,
            @AssigneeUserId, 'pending', NULL, @InputJson, @InputJson, 1, @Now, NULL, NULL
        FROM fn_workflow_vote AS vote
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=vote.InstanceId
        INNER JOIN fn_workflow_todo AS todo ON todo.StepId=vote.Id AND todo.InstanceId=instance.Id AND todo.Id=@TodoId
        WHERE vote.Id=@VoteId AND instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey
          AND todo.AssigneeUserId=@AssigneeUserId AND todo.StatusKey='active' AND vote.StatusKey='active'
        """, SqlDataScope.Global);

    private const string RoundProjection = """
        SELECT vote.Id, vote.InstanceId, vote.FormBindingId, vote.MemberCount, vote.ParticipationPercentage,
            vote.ApprovalPercentage, vote.AllowAbstain, vote.ClosesAtUtc, vote.StatusKey, vote.Revision,
            vote.FinalYesCount, vote.FinalNoCount, vote.FinalAbstainCount, vote.FinalUnsubmittedCount
        FROM fn_workflow_vote AS vote
        INNER JOIN fn_workflow_step AS step ON step.Id=vote.Id AND step.InstanceId=vote.InstanceId
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=vote.InstanceId
        INNER JOIN fn_workflow_vote_form AS binding ON binding.Id=vote.FormBindingId
            AND binding.DefinitionVersionId=instance.DefinitionVersionId AND binding.NodeKey=step.NodeKey
        WHERE vote.Id=@VoteId AND vote.InstanceId=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey
        """;
    public static readonly SqlStatement FindRound = new("workflow.vote.find_round", RoundProjection, SqlDataScope.Global);

    public static readonly SqlStatement FindBallot = new("workflow.vote.find_ballot", """
        SELECT ballot.Id, ballot.VoteId, ballot.TodoId, ballot.AssigneeUserId, ballot.StatusKey,
            ballot.ChoiceKey, ballot.InputJson, ballot.SubmissionJson, ballot.Revision
        FROM fn_workflow_vote_ballot AS ballot
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=ballot.InstanceId
        WHERE ballot.VoteId=@VoteId AND ballot.InstanceId=@InstanceId AND ballot.TodoId=@TodoId
          AND ballot.AssigneeUserId=@ActorUserId AND instance.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);

    public static readonly SqlStatement FindView = new("workflow.vote.find_view", """
        SELECT viewRecord.InputJson, viewRecord.InputRevision, viewRecord.OutputJson, viewRecord.OutputRevision
        FROM fn_workflow_vote_view AS viewRecord
        INNER JOIN fn_workflow_todo AS todo ON todo.Id=viewRecord.Id AND todo.InstanceId=viewRecord.InstanceId
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=todo.InstanceId
        WHERE todo.Id=@TodoId AND todo.AssigneeUserId=@ActorUserId AND todo.StepId=@VoteId
          AND instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);

    public static readonly SqlStatement CaptureView = new("workflow.vote.capture_view", """
        INSERT INTO fn_workflow_vote_view (Id, BallotId, InstanceId, InputJson, InputRevision, OutputJson, OutputRevision)
        SELECT todo.Id, ballot.Id, instance.Id, ballot.SubmissionJson, ballot.Revision, NULL, NULL
        FROM fn_workflow_vote_ballot AS ballot
        INNER JOIN fn_workflow_todo AS todo ON todo.Id=ballot.TodoId AND todo.InstanceId=ballot.InstanceId AND todo.StepId=ballot.VoteId
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=ballot.InstanceId
        WHERE todo.Id=@TodoId AND instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey
          AND ballot.StatusKey='pending' AND todo.StatusKey='active' AND todo.AssigneeUserId=ballot.AssigneeUserId
        """, SqlDataScope.Global);

    public static readonly SqlStatement SetViewOutput = new("workflow.vote.set_view_output", """
        UPDATE fn_workflow_vote_view SET OutputJson=@OutputJson, OutputRevision=@OutputRevision
        WHERE Id=@TodoId AND BallotId=@BallotId AND InstanceId=@InstanceId AND OutputJson IS NULL AND OutputRevision IS NULL
          AND InputRevision<@OutputRevision AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance
            WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);

    public static readonly SqlStatement SubmitBallot = new("workflow.vote.submit_ballot", """
        UPDATE fn_workflow_vote_ballot SET StatusKey='submitted', ChoiceKey=@ChoiceKey,
            SubmissionJson=@SubmissionJson, Revision=Revision+1, SubmittedAtUtc=@Now
        WHERE Id=@Id AND VoteId=@VoteId AND InstanceId=@InstanceId AND TodoId=@TodoId
          AND AssigneeUserId=@ActorUserId AND Revision=@Revision AND StatusKey='pending'
          AND EXISTS (SELECT 1 FROM fn_workflow_vote AS vote
            INNER JOIN fn_workflow_instance AS instance ON instance.Id=vote.InstanceId
            WHERE vote.Id=@VoteId AND vote.InstanceId=@InstanceId AND vote.StatusKey='active'
              AND (vote.ClosesAtUtc IS NULL OR vote.ClosesAtUtc>@Now)
              AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);

    public static readonly SqlStatement CountOtherMember = new("workflow.vote.count_other_member", """
        SELECT COUNT(*) FROM fn_workflow_vote_ballot AS ballot
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=ballot.InstanceId
        WHERE ballot.VoteId=@VoteId AND ballot.InstanceId=@InstanceId AND ballot.Id<>@ExcludedBallotId
          AND ballot.AssigneeUserId=@AssigneeUserId AND instance.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);

    public static readonly SqlStatement TransferBallot = new("workflow.vote.transfer_ballot", """
        UPDATE fn_workflow_vote_ballot SET TodoId=@NewTodoId, AssigneeUserId=@AssigneeUserId, Revision=Revision+1
        WHERE Id=@Id AND VoteId=@VoteId AND InstanceId=@InstanceId AND TodoId=@TodoId
          AND AssigneeUserId=@ActorUserId AND Revision=@Revision AND StatusKey='pending'
          AND EXISTS (SELECT 1 FROM fn_workflow_todo AS todo
            INNER JOIN fn_workflow_instance AS instance ON instance.Id=todo.InstanceId
            WHERE todo.Id=@NewTodoId AND todo.StepId=@VoteId AND todo.InstanceId=@InstanceId AND todo.StatusKey='active'
              AND todo.AssigneeUserId=@AssigneeUserId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);

    private const string CountSource = " FROM fn_workflow_vote_ballot WHERE VoteId=@VoteId AND InstanceId=@InstanceId";
    public static readonly SqlStatement TallySqlServer = new("workflow.vote.tally.sqlserver", """
        SELECT CAST(COALESCE(SUM(CASE WHEN ChoiceKey='yes' THEN 1 ELSE 0 END),0) AS bigint) AS YesCount,
            CAST(COALESCE(SUM(CASE WHEN ChoiceKey='no' THEN 1 ELSE 0 END),0) AS bigint) AS NoCount,
            CAST(COALESCE(SUM(CASE WHEN ChoiceKey='abstain' THEN 1 ELSE 0 END),0) AS bigint) AS AbstainCount,
            CAST(COALESCE(SUM(CASE WHEN StatusKey='pending' THEN 1 ELSE 0 END),0) AS bigint) AS PendingCount,
            CAST(COALESCE(SUM(CASE WHEN StatusKey='cancelled' THEN 1 ELSE 0 END),0) AS bigint) AS CancelledCount
        """ + CountSource, SqlDataScope.Global);
    public static readonly SqlStatement TallyMySql = new("workflow.vote.tally.mysql", """
        SELECT CAST(COALESCE(SUM(CASE WHEN ChoiceKey='yes' THEN 1 ELSE 0 END),0) AS SIGNED) AS YesCount,
            CAST(COALESCE(SUM(CASE WHEN ChoiceKey='no' THEN 1 ELSE 0 END),0) AS SIGNED) AS NoCount,
            CAST(COALESCE(SUM(CASE WHEN ChoiceKey='abstain' THEN 1 ELSE 0 END),0) AS SIGNED) AS AbstainCount,
            CAST(COALESCE(SUM(CASE WHEN StatusKey='pending' THEN 1 ELSE 0 END),0) AS SIGNED) AS PendingCount,
            CAST(COALESCE(SUM(CASE WHEN StatusKey='cancelled' THEN 1 ELSE 0 END),0) AS SIGNED) AS CancelledCount
        """ + CountSource, SqlDataScope.Global);

    public static readonly SqlStatement CloseRound = new("workflow.vote.close_round", """
        UPDATE fn_workflow_vote SET StatusKey=@StatusKey, Revision=Revision+1, CompletedAtUtc=@Now,
            FinalYesCount=@YesCount, FinalNoCount=@NoCount, FinalAbstainCount=@AbstainCount, FinalUnsubmittedCount=@UnsubmittedCount
        WHERE Id=@VoteId AND InstanceId=@InstanceId AND StatusKey='active' AND Revision=@Revision
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId
            AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);

    public static readonly SqlStatement CancelPendingTodos = new("workflow.vote.cancel_pending_todos", """
        UPDATE fn_workflow_todo SET StatusKey='cancelled', ResultActionKey='cancelled', CompletedAtUtc=@Now, Revision=Revision+1
        WHERE InstanceId=@InstanceId AND StepId=@VoteId AND StatusKey='active'
          AND Id IN (SELECT TodoId FROM fn_workflow_vote_ballot WHERE VoteId=@VoteId AND InstanceId=@InstanceId AND StatusKey='pending')
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId
            AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelPendingBallots = new("workflow.vote.cancel_pending_ballots", """
        UPDATE fn_workflow_vote_ballot SET StatusKey='cancelled', CancelledAtUtc=@Now, Revision=Revision+1
        WHERE VoteId=@VoteId AND InstanceId=@InstanceId AND StatusKey='pending'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId
            AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """, SqlDataScope.Global);

    public static readonly SqlStatement CancelRoundsByInstance = new("workflow.vote.cancel_rounds_by_instance", """
        UPDATE fn_workflow_vote SET StatusKey='cancelled', Revision=Revision+1, CompletedAtUtc=@Now,
            FinalYesCount=(SELECT COUNT(*) FROM fn_workflow_vote_ballot AS ballot WHERE ballot.VoteId=fn_workflow_vote.Id AND ballot.InstanceId=@InstanceId AND ballot.ChoiceKey='yes'),
            FinalNoCount=(SELECT COUNT(*) FROM fn_workflow_vote_ballot AS ballot WHERE ballot.VoteId=fn_workflow_vote.Id AND ballot.InstanceId=@InstanceId AND ballot.ChoiceKey='no'),
            FinalAbstainCount=(SELECT COUNT(*) FROM fn_workflow_vote_ballot AS ballot WHERE ballot.VoteId=fn_workflow_vote.Id AND ballot.InstanceId=@InstanceId AND ballot.ChoiceKey='abstain'),
            FinalUnsubmittedCount=(SELECT COUNT(*) FROM fn_workflow_vote_ballot AS ballot WHERE ballot.VoteId=fn_workflow_vote.Id AND ballot.InstanceId=@InstanceId AND ballot.ChoiceKey IS NULL)
        WHERE InstanceId=@InstanceId AND StatusKey='active'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey)
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelBallotsByInstance = new("workflow.vote.cancel_ballots_by_instance", """
        UPDATE fn_workflow_vote_ballot SET StatusKey='cancelled', Revision=Revision+1, CancelledAtUtc=@Now
        WHERE InstanceId=@InstanceId AND StatusKey='pending'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey)
        """, SqlDataScope.Global);

    private const string DueSource = """
        FROM fn_workflow_vote AS vote
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=vote.InstanceId
        WHERE vote.StatusKey='active' AND vote.ClosesAtUtc IS NOT NULL AND vote.ClosesAtUtc<=@Now AND instance.StatusKey='active'
        ORDER BY vote.ClosesAtUtc, vote.Id
        """;
    public static readonly SqlStatement FindDueSqlServer = new("workflow.vote.find_due.sqlserver", "SELECT TOP (@BatchSize) vote.Id, vote.InstanceId, instance.TenantScopeKey " + DueSource, SqlDataScope.Global);
    public static readonly SqlStatement FindDueMySql = new("workflow.vote.find_due.mysql", "SELECT vote.Id, vote.InstanceId, instance.TenantScopeKey " + DueSource + " LIMIT @BatchSize", SqlDataScope.Global);
}

/// <summary>独立表单绑定与架构摘要来自同一不可变发布版本。</summary>
internal sealed record WorkflowVoteFormBindingRecord(Guid Id, Guid BallotFormVersionId, string FormSchemaJson, string FormSchemaHash, string FormStatusKey);
/// <summary>发布前一次有界批量读取同一可信作用域中的实际投票表单，不能逐节点回退到外部租户。</summary>
internal sealed record WorkflowVoteFormCandidateRecord(Guid Id, string FormSchemaJson, string FormStatusKey);
/// <summary>活动级固定规则及最终汇总，不包含任何人的个人表单答案。</summary>
internal sealed record WorkflowVoteRoundRecord(Guid Id, Guid InstanceId, Guid FormBindingId, int MemberCount,
    int ParticipationPercentage, int ApprovalPercentage, bool AllowAbstain, DateTimeOffset? ClosesAtUtc, string StatusKey,
    long Revision, int? FinalYesCount, int? FinalNoCount, int? FinalAbstainCount, int? FinalUnsubmittedCount);
/// <summary>当前个人票据写入上下文；历史读取必须另取该次Todo的私有视图。</summary>
internal sealed record WorkflowVoteBallotRecord(Guid Id, Guid VoteId, Guid TodoId, Guid AssigneeUserId, string StatusKey,
    string? ChoiceKey, string InputJson, string SubmissionJson, long Revision);
/// <summary>数据库投影显式转为有符号长整数，避免MySQL的SUM decimal推断。</summary>
internal sealed record WorkflowVoteCountsRecord(long YesCount, long NoCount, long AbstainCount, long PendingCount, long CancelledCount);
/// <summary>后台只以持久事实中的可信作用域派发截止处理，不接受请求传入租户。</summary>
internal sealed record WorkflowDueVoteRecord(Guid Id, Guid InstanceId, string TenantScopeKey);
/// <summary>后台定位既有活动上下文，不作为投票执行人或新选票来源。</summary>
internal sealed record WorkflowVoteDeadlineMemberRecord(Guid TodoId, Guid AssigneeUserId);
