using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>活动输入、启动回执及耐久调度使用的模块内 SQL。</summary>
internal static class WorkflowActivitySql
{
    public static readonly SqlStatement CountActivations = new("workflow.activity.count_activations", """
        SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId = @InstanceId
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountRecoverableWork = new("workflow.activity_work.count_recoverable", """
        SELECT COUNT(*) FROM fn_workflow_activity_work
        WHERE InstanceId = @InstanceId AND TenantScopeKey = @TenantScopeKey AND StatusKey IN ('pending', 'leased', 'faulted')
        """, SqlDataScope.Global);
    public static readonly SqlStatement RequeueFaultedWork = new("workflow.activity_work.requeue_faulted", """
        UPDATE fn_workflow_activity_work SET StatusKey = 'pending', DueAtUtc = @Now,
            LeaseGeneration = LeaseGeneration + 1, AttemptCount = 0, CompletedAtUtc = NULL,
            ResultStatusKey = NULL, ResultTodoId = NULL, LeaseOwnerKey = NULL, LeaseExpiresAtUtc = NULL
        WHERE InstanceId = @InstanceId AND TenantScopeKey = @TenantScopeKey AND StatusKey = 'faulted'
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountOpenWork = new("workflow.activity_work.count_open", """
        SELECT COUNT(*) FROM fn_workflow_activity_work
        WHERE InstanceId = @InstanceId AND TenantScopeKey = @TenantScopeKey AND StatusKey IN ('pending', 'leased')
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelApprovalSlots = new("workflow.approval_slot.cancel_instance", """
        UPDATE fn_workflow_approval_slot SET DecisionKey = 'cancelled', DecidedAtUtc = @Now, Revision = Revision + 1
        WHERE DecisionKey IS NULL AND StepId IN (SELECT Id FROM fn_workflow_step WHERE InstanceId = @InstanceId)
        """, SqlDataScope.Global);
    private const string WorkProjection = "work.Id, work.InstanceId, work.TenantScopeKey, work.NodeKey, work.InputJson, work.InputHash, work.InputFormRevision, work.ExecutionSequence, work.ParallelJoinId, work.ParallelBranchKey, work.LeaseGeneration, work.AttemptCount, work.ResumeBarrierId";

    public static readonly SqlStatement InsertWork = new("workflow.activity_work.insert", """
        INSERT INTO fn_workflow_activity_work
            (Id, InstanceId, TenantScopeKey, NodeKey, InputJson, InputHash, InputFormRevision,
             ExecutionSequence, ParallelJoinId, ParallelBranchKey, DueAtUtc, StatusKey, LeaseGeneration, AttemptCount, ResumeBarrierId)
        VALUES (@Id, @InstanceId, @TenantScopeKey, @NodeKey, @InputJson, @InputHash, @InputFormRevision,
            @ExecutionSequence, @ParallelJoinId, @ParallelBranchKey, @DueAtUtc, 'pending', 0, 0, @ResumeBarrierId)
        """, SqlDataScope.Global);

    private const string DueWork = """
        FROM fn_workflow_activity_work AS work
        INNER JOIN fn_workflow_instance AS instance ON instance.Id = work.InstanceId
          AND instance.TenantScopeKey = work.TenantScopeKey
        WHERE instance.StatusKey = 'active' AND work.DueAtUtc <= @Now
          AND (work.StatusKey = 'pending' OR (work.StatusKey = 'leased' AND work.LeaseExpiresAtUtc <= @Now))
        ORDER BY work.DueAtUtc, work.Id
        """;
    public static readonly SqlStatement ListDueSqlServer = new("workflow.activity_work.due.sqlserver",
        "SELECT TOP (@Take) " + WorkProjection + " " + DueWork, SqlDataScope.Global);
    public static readonly SqlStatement ListDueMySql = new("workflow.activity_work.due.mysql",
        "SELECT " + WorkProjection + " " + DueWork + " LIMIT @Take", SqlDataScope.Global);

    public static readonly SqlStatement ClaimWork = new("workflow.activity_work.claim", """
        UPDATE fn_workflow_activity_work SET StatusKey = 'leased', LeaseOwnerKey = @LeaseOwnerKey,
            LeaseExpiresAtUtc = @LeaseExpiresAtUtc, LeaseGeneration = LeaseGeneration + 1, AttemptCount = AttemptCount + 1
        WHERE Id = @Id AND LeaseGeneration = @LeaseGeneration AND DueAtUtc <= @Now
          AND (StatusKey = 'pending' OR (StatusKey = 'leased' AND LeaseExpiresAtUtc <= @Now))
        """, SqlDataScope.Global);

    public static readonly SqlStatement FinishWork = new("workflow.activity_work.finish", """
        UPDATE fn_workflow_activity_work SET StatusKey = @StatusKey, CompletedAtUtc = @Now,
            ResultStatusKey = @ResultStatusKey, ResultTodoId = @ResultTodoId,
            LeaseOwnerKey = NULL, LeaseExpiresAtUtc = NULL
        WHERE Id = @Id AND StatusKey = 'leased' AND LeaseOwnerKey = @LeaseOwnerKey
          AND LeaseGeneration = @LeaseGeneration AND LeaseExpiresAtUtc > @Now
        """, SqlDataScope.Global);

    public static readonly SqlStatement CancelWork = new("workflow.activity_work.cancel", """
        UPDATE fn_workflow_activity_work SET StatusKey = 'cancelled', CompletedAtUtc = @Now,
            LeaseGeneration = LeaseGeneration + 1, LeaseOwnerKey = NULL, LeaseExpiresAtUtc = NULL
        WHERE InstanceId = @InstanceId AND TenantScopeKey = @TenantScopeKey AND StatusKey IN ('pending', 'leased')
        """, SqlDataScope.Global);

    public static readonly SqlStatement FindStartReceipt = new(
        "workflow.start_receipt.find",
        """
        SELECT RequestHash, ResponseJson FROM fn_workflow_start_receipt
        WHERE TenantScopeKey = @TenantScopeKey AND ActorUserId = @ActorUserId
          AND IdempotencyKey = @IdempotencyKey
        """, SqlDataScope.Global);

    public static readonly SqlStatement InsertStartReceipt = new(
        "workflow.start_receipt.insert",
        """
        INSERT INTO fn_workflow_start_receipt
            (InstanceId, TenantScopeKey, ActorUserId, IdempotencyKey, RequestHash, ResponseJson)
        VALUES (@InstanceId, @TenantScopeKey, @ActorUserId, @IdempotencyKey, @RequestHash, @ResponseJson)
        """, SqlDataScope.Global);
}

/// <summary>不可变启动响应；运行状态变化不影响幂等重放。</summary>
internal sealed record WorkflowStartReceiptRecord(string RequestHash, string ResponseJson);

/// <summary>续办活动的固定输入及当前领取代次；重领不得变更输入或工作标识。</summary>
internal sealed record WorkflowActivityWorkRecord(Guid Id, Guid InstanceId, string TenantScopeKey,
    string NodeKey, string InputJson, string InputHash, long InputFormRevision, long ExecutionSequence,
    Guid? ParallelJoinId, string? ParallelBranchKey, long LeaseGeneration, int AttemptCount, Guid? ResumeBarrierId = null);
