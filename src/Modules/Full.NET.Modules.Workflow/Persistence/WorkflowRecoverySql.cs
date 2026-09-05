using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>恢复任务扫描、领取、续租与对账 SQL；Worker 扫描不加租户过滤，管理查询必须携带 TenantScopeKey。</summary>
internal static class WorkflowRecoverySql
{
    private const string TaskColumns = """
        Id, TenantId, ScopeKey, TenantScopeKey, InstanceId, StepId, KindKey, StatusKey,
        AttemptCount, Revision, LeaseOwnerKey, LeaseExpiresAtUtc, LeaseGeneration,
        NextAttemptAtUtc, LastError, CreatedAtUtc, UpdatedAtUtc
        """;

    public static readonly SqlStatement ScanExpiredLeases = new(
        "workflow.recovery.scan_expired_leases",
        """
        SELECT instance.TenantId, instance.ScopeKey, instance.TenantScopeKey, instance.Id, NULL
        FROM fn_workflow_instance AS instance
        WHERE instance.StatusKey = 'active'
          AND instance.LeaseExpiresAtUtc IS NOT NULL
          AND instance.LeaseExpiresAtUtc <= @Now
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ScanStuckInstances = new(
        "workflow.recovery.scan_stuck_instances",
        """
        SELECT instance.TenantId, instance.ScopeKey, instance.TenantScopeKey, instance.Id, NULL
        FROM fn_workflow_instance AS instance
        WHERE instance.StatusKey = 'active'
          AND (instance.LeaseExpiresAtUtc IS NULL OR instance.LeaseExpiresAtUtc <= @Now)
          AND NOT EXISTS (
              SELECT 1
              FROM fn_workflow_todo AS todo
              WHERE todo.InstanceId = instance.Id
                AND todo.StatusKey = 'active')
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ScanIncompleteSteps = new(
        "workflow.recovery.scan_incomplete_steps",
        """
        SELECT instance.TenantId, instance.ScopeKey, instance.TenantScopeKey, instance.Id, step.Id
        FROM fn_workflow_step AS step
        INNER JOIN fn_workflow_instance AS instance ON instance.Id = step.InstanceId
        WHERE instance.StatusKey = 'active'
          AND step.StatusKey = 'active'
          AND step.NodeTypeKey = 'human.approval'
          AND NOT EXISTS (
              SELECT 1
              FROM fn_workflow_todo AS todo
              WHERE todo.StepId = step.Id
                AND todo.StatusKey = 'active')
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertOpenTask = new(
        "workflow.recovery.insert_open_task",
        """
        INSERT INTO fn_workflow_recovery_task
            (Id, TenantId, ScopeKey, TenantScopeKey, InstanceId, StepId, KindKey, StatusKey,
             AttemptCount, Revision, LeaseOwnerKey, LeaseExpiresAtUtc, LeaseGeneration,
             NextAttemptAtUtc, LastError, CreatedAtUtc, UpdatedAtUtc)
        SELECT
            @Id, @TenantId, @ScopeKey, @TenantScopeKey, @InstanceId, @StepId, @KindKey, 'pending',
            0, 1, NULL, NULL, 0, NULL, NULL, @CreatedAtUtc, @UpdatedAtUtc
        WHERE NOT EXISTS (
            SELECT 1
            FROM fn_workflow_recovery_task AS existing
            WHERE existing.TenantScopeKey = @TenantScopeKey
              AND existing.InstanceId = @InstanceId
              AND existing.KindKey = @KindKey
              AND ((existing.StepId IS NULL AND @StepId IS NULL) OR existing.StepId = @StepId)
              AND existing.StatusKey IN ('pending', 'failed', 'dead_lettered'))
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ClaimTasksSqlServer = new(
        "workflow.recovery.claim.sql_server",
        """
        ;WITH Pending AS
        (
            SELECT TOP (@BatchSize) *
            FROM fn_workflow_recovery_task WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE StatusKey IN ('pending', 'failed')
              AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= @Now)
              AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc <= @Now)
            ORDER BY NextAttemptAtUtc, CreatedAtUtc, Id
        )
        UPDATE Pending
        SET LeaseOwnerKey = @LeaseOwnerKey,
            LeaseExpiresAtUtc = @LeaseExpiresAtUtc,
            LeaseGeneration = LeaseGeneration + 1,
            Revision = Revision + 1,
            UpdatedAtUtc = @Now
        OUTPUT inserted.Id, inserted.TenantId, inserted.ScopeKey, inserted.TenantScopeKey,
               inserted.InstanceId, inserted.StepId, inserted.KindKey, inserted.StatusKey,
               inserted.AttemptCount, inserted.Revision, inserted.LeaseOwnerKey,
               inserted.LeaseExpiresAtUtc, inserted.LeaseGeneration, inserted.NextAttemptAtUtc,
               inserted.LastError, inserted.CreatedAtUtc, inserted.UpdatedAtUtc;
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement SelectClaimableTaskIdsMySql = new(
        "workflow.recovery.select_claimable_ids.mysql",
        """
        SELECT Id
        FROM fn_workflow_recovery_task
        WHERE StatusKey IN ('pending', 'failed')
          AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= @Now)
          AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc <= @Now)
        ORDER BY NextAttemptAtUtc, CreatedAtUtc, Id
        LIMIT @BatchSize
        FOR UPDATE SKIP LOCKED
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ClaimTasksByIdsMySql = new(
        "workflow.recovery.claim_by_ids.mysql",
        """
        UPDATE fn_workflow_recovery_task
        SET LeaseOwnerKey = @LeaseOwnerKey,
            LeaseExpiresAtUtc = @LeaseExpiresAtUtc,
            LeaseGeneration = LeaseGeneration + 1,
            Revision = Revision + 1,
            UpdatedAtUtc = @Now
        WHERE Id IN @Ids
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement SelectTasksByLease = new(
        "workflow.recovery.select_by_lease",
        $"""
        SELECT {TaskColumns}
        FROM fn_workflow_recovery_task
        WHERE LeaseOwnerKey = @LeaseOwnerKey
        ORDER BY CreatedAtUtc, Id
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement RenewTaskLease = new(
        "workflow.recovery.renew_lease",
        """
        UPDATE fn_workflow_recovery_task
        SET LeaseExpiresAtUtc = @LeaseExpiresAtUtc,
            LeaseGeneration = LeaseGeneration + 1,
            Revision = Revision + 1,
            UpdatedAtUtc = @Now
        WHERE Id = @Id
          AND LeaseOwnerKey = @LeaseOwnerKey
          AND LeaseGeneration = @LeaseGeneration
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CompleteTask = new(
        "workflow.recovery.complete",
        """
        UPDATE fn_workflow_recovery_task
        SET StatusKey = @StatusKey,
            AttemptCount = @AttemptCount,
            LastError = @LastError,
            NextAttemptAtUtc = @NextAttemptAtUtc,
            LeaseOwnerKey = NULL,
            LeaseExpiresAtUtc = NULL,
            Revision = Revision + 1,
            UpdatedAtUtc = @Now
        WHERE Id = @Id
          AND LeaseOwnerKey = @LeaseOwnerKey
          AND LeaseGeneration = @LeaseGeneration
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ReclaimInstanceLease = new(
        "workflow.recovery.reclaim_instance_lease",
        """
        UPDATE fn_workflow_instance
        SET LeaseOwnerKey = @LeaseOwnerKey,
            LeaseExpiresAtUtc = @LeaseExpiresAtUtc
        WHERE Id = @Id
          AND StatusKey = 'active'
          AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc <= @Now)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ClearInstanceLease = new(
        "workflow.recovery.clear_instance_lease",
        """
        UPDATE fn_workflow_instance
        SET LeaseOwnerKey = NULL,
            LeaseExpiresAtUtc = NULL
        WHERE Id = @Id
          AND (LeaseOwnerKey = @LeaseOwnerKey OR LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc <= @Now)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindActiveTodoByStep = new(
        "workflow.recovery.find_active_todo_by_step",
        """
        SELECT todo.Id, todo.InstanceId, todo.StepId, todo.AssigneeUserId,
               todo.StatusKey, todo.ArrivedAtUtc, todo.CompletedAtUtc,
               todo.ResultActionKey, todo.Revision
        FROM fn_workflow_todo AS todo
        INNER JOIN fn_workflow_instance AS instance ON instance.Id = todo.InstanceId
        WHERE todo.StepId = @StepId
          AND todo.StatusKey = 'active'
          AND instance.TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindTaskById = new(
        "workflow.recovery.find_by_id",
        $"""
        SELECT {TaskColumns}
        FROM fn_workflow_recovery_task
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CountTasksForScope = new(
        "workflow.recovery.count_for_scope",
        """
        SELECT COUNT(*)
        FROM fn_workflow_recovery_task
        WHERE TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListTasksForScopeSqlServer = new(
        "workflow.recovery.list_for_scope.sql_server",
        $"""
        SELECT {TaskColumns}
        FROM fn_workflow_recovery_task
        WHERE TenantScopeKey = @TenantScopeKey
        ORDER BY CreatedAtUtc DESC, Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListTasksForScopeMySql = new(
        "workflow.recovery.list_for_scope.mysql",
        $"""
        SELECT {TaskColumns}
        FROM fn_workflow_recovery_task
        WHERE TenantScopeKey = @TenantScopeKey
        ORDER BY CreatedAtUtc DESC, Id DESC
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement RequeueTask = new(
        "workflow.recovery.requeue",
        """
        UPDATE fn_workflow_recovery_task
        SET StatusKey = 'pending',
            AttemptCount = 0,
            LastError = NULL,
            NextAttemptAtUtc = @NextAttemptAtUtc,
            LeaseOwnerKey = NULL,
            LeaseExpiresAtUtc = NULL,
            Revision = Revision + 1,
            UpdatedAtUtc = @Now
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND Revision = @Revision
          AND StatusKey IN ('failed', 'dead_lettered')
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement MarkTaskSucceeded = new(
        "workflow.recovery.mark_succeeded",
        """
        UPDATE fn_workflow_recovery_task
        SET StatusKey = 'succeeded',
            LastError = NULL,
            NextAttemptAtUtc = NULL,
            LeaseOwnerKey = NULL,
            LeaseExpiresAtUtc = NULL,
            Revision = Revision + 1,
            UpdatedAtUtc = @Now
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND Revision = @Revision
          AND StatusKey IN ('pending', 'failed', 'dead_lettered')
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement MarkTaskCancelled = new(
        "workflow.recovery.mark_cancelled",
        """
        UPDATE fn_workflow_recovery_task
        SET StatusKey = 'cancelled',
            NextAttemptAtUtc = NULL,
            LeaseOwnerKey = NULL,
            LeaseExpiresAtUtc = NULL,
            Revision = Revision + 1,
            UpdatedAtUtc = @Now
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND Revision = @Revision
          AND StatusKey IN ('pending', 'failed', 'dead_lettered')
        """,
        SqlDataScope.Global);
}
