using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>恢复任务扫描、领取、续租与对账 SQL；Worker 扫描不加租户过滤，管理查询必须携带 TenantScopeKey。</summary>
internal static class WorkflowRecoverySql
{
    // 只为卡住任务对账：真实活动等待已经具备耐久续办来源，不能因缺少人工Todo被死信暂停。
    public static readonly SqlStatement HasDurableWaitSqlServer = new("workflow.recovery.has_durable_wait.sql_server", DurableWaitText(false), SqlDataScope.Global);
    public static readonly SqlStatement HasDurableWaitMySql = new("workflow.recovery.has_durable_wait.mysql", DurableWaitText(true), SqlDataScope.Global);

    private static string DurableWaitText(bool mysql) => """
        SELECT COUNT(*) FROM fn_workflow_instance AS instance
        WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active'
            AND NOT (1=1
        """ + NoCollaborationWait(mysql) + NoSubprocessWait(mysql) + ")";

    // 审计必须对应刚完成的真实任务与租约代次，不能只凭调用方提交的JSON声明系统身份。
    public static readonly SqlStatement InsertSystemAudit = new("workflow.recovery.system_audit", """
        INSERT INTO fn_workflow_domain_audit
            (Id,TenantId,ScopeKey,InstanceId,OperationKey,ActorUserId,ResourceTypeKey,ResourceId,OutcomeKey,DetailJson,CreatedAtUtc,CorrelationJson)
        SELECT @Id,task.TenantId,task.ScopeKey,task.InstanceId,@OperationKey,NULL,'recovery_task',task.Id,task.StatusKey,@DetailJson,@CreatedAtUtc,@CorrelationJson
        FROM fn_workflow_recovery_task AS task
        WHERE task.Id=@ResourceId AND task.InstanceId=@InstanceId AND task.TenantScopeKey = @TenantScopeKey
            AND task.KindKey=@KindKey AND task.LeaseGeneration=@LeaseGeneration AND task.AttemptCount=@AttemptCount AND task.StatusKey=@OutcomeKey
        """, SqlDataScope.Global);

    private const string TaskColumns = """
        Id, TenantId, ScopeKey, TenantScopeKey, InstanceId, StepId, KindKey, StatusKey,
        AttemptCount, Revision, LeaseOwnerKey, LeaseExpiresAtUtc, LeaseGeneration,
        NextAttemptAtUtc, LastError, CreatedAtUtc, UpdatedAtUtc
        """;

    public static readonly SqlStatement ScanExpiredLeasesSqlServer = new(
        "workflow.recovery.scan_expired_leases.sql_server",
        CreateScanText(false, false,
            "instance.LeaseExpiresAtUtc IS NOT NULL AND instance.LeaseExpiresAtUtc <= @Now"),
        SqlDataScope.Global);
    public static readonly SqlStatement ScanExpiredLeasesMySql = new(
        "workflow.recovery.scan_expired_leases.mysql",
        CreateScanText(true, false,
            "instance.LeaseExpiresAtUtc IS NOT NULL AND instance.LeaseExpiresAtUtc <= @Now"),
        SqlDataScope.Global);
    public static readonly SqlStatement ScanStuckInstancesSqlServer = new(
        "workflow.recovery.scan_stuck_instances.sql_server",
        CreateScanText(false, false,
            "(instance.LeaseExpiresAtUtc IS NULL OR instance.LeaseExpiresAtUtc <= @Now) AND NOT EXISTS (SELECT 1 FROM fn_workflow_activity_work work WHERE work.InstanceId=instance.Id AND work.StatusKey IN ('pending','leased')) AND NOT EXISTS (SELECT 1 FROM fn_workflow_todo todo WHERE todo.InstanceId=instance.Id AND todo.StatusKey='active')" + NoCollaborationWait(false) + NoSubprocessWait(false)),
        SqlDataScope.Global);
    public static readonly SqlStatement ScanStuckInstancesMySql = new(
        "workflow.recovery.scan_stuck_instances.mysql",
        CreateScanText(true, false,
            "(instance.LeaseExpiresAtUtc IS NULL OR instance.LeaseExpiresAtUtc <= @Now) AND NOT EXISTS (SELECT 1 FROM fn_workflow_activity_work work WHERE work.InstanceId=instance.Id AND work.StatusKey IN ('pending','leased')) AND NOT EXISTS (SELECT 1 FROM fn_workflow_todo todo WHERE todo.InstanceId=instance.Id AND todo.StatusKey='active')" + NoCollaborationWait(true) + NoSubprocessWait(true)),
        SqlDataScope.Global);
    public static readonly SqlStatement ScanIncompleteStepsSqlServer = new(
        "workflow.recovery.scan_incomplete_steps.sql_server",
        CreateScanText(false, true,
            "step.StatusKey='active' AND step.NodeTypeKey IN ('human.approval','human.group_approval','human.task') AND NOT EXISTS (SELECT 1 FROM fn_workflow_todo todo WHERE todo.StepId=step.Id AND todo.StatusKey='active')"),
        SqlDataScope.Global);
    public static readonly SqlStatement ScanIncompleteStepsMySql = new(
        "workflow.recovery.scan_incomplete_steps.mysql",
        CreateScanText(true, true,
            "step.StatusKey='active' AND step.NodeTypeKey IN ('human.approval','human.group_approval','human.task') AND NOT EXISTS (SELECT 1 FROM fn_workflow_todo todo WHERE todo.StepId=step.Id AND todo.StatusKey='active')"),
        SqlDataScope.Global);

    // 协作在真实活动步骤上耐久等待，对端尚无结果并不等于缺少本人的待办。
    private static string NoCollaborationWait(bool mysql) => """
         AND NOT EXISTS (SELECT 1 FROM fn_workflow_collaboration_call AS callState
            INNER JOIN fn_workflow_step AS waitingStep ON waitingStep.Id=callState.Id AND waitingStep.InstanceId=callState.InstanceId
            WHERE callState.InstanceId=instance.Id
        """ + " AND " + (mysql ? "callState.TenantScopeKey=instance.TenantScopeKey" : "callState.TenantScopeKey COLLATE Latin1_General_100_BIN2=instance.TenantScopeKey COLLATE Latin1_General_100_BIN2") + """
                AND callState.StatusKey IN ('preparing','requested','accepted') AND waitingStep.StatusKey='active' AND waitingStep.NodeTypeKey='process.collaboration')
        """;

    // 本地父子协议同样以真实活动Step为证，损坏、已关闭或无步骤的调用不能屏蔽恢复。
    private static string NoSubprocessWait(bool mysql) => """
         AND NOT EXISTS (SELECT 1 FROM fn_workflow_subprocess_call AS callState
            INNER JOIN fn_workflow_step AS waitingStep ON waitingStep.Id=callState.Id AND waitingStep.InstanceId=callState.InstanceId
            WHERE callState.InstanceId=instance.Id
        """ + " AND " + (mysql ? "callState.TenantScopeKey=instance.TenantScopeKey" : "callState.TenantScopeKey COLLATE Latin1_General_100_BIN2=instance.TenantScopeKey COLLATE Latin1_General_100_BIN2") + """
                AND callState.StatusKey IN ('requested','running','result_ready') AND waitingStep.StatusKey='active' AND waitingStep.NodeTypeKey='process.subprocess')
        """;

    // 扫描仅返回本模块尚未占用的有界候选；插入端仍须去重以覆盖多 Worker 的竞争窗口。
    private static string CreateScanText(bool mysql, bool steps, string predicate) =>
        "SELECT " + (mysql ? "" : "TOP (@BatchSize) ")
        + "instance.TenantId, instance.ScopeKey, instance.TenantScopeKey, instance.Id AS InstanceId, "
        + (steps ? "step.Id" : "NULLIF(instance.Id, instance.Id)") + " AS StepId "
        + "FROM fn_workflow_instance AS instance "
        + (steps ? "INNER JOIN fn_workflow_step AS step ON step.InstanceId=instance.Id " : "")
        + "WHERE instance.StatusKey='active' AND " + predicate
        + " AND NOT EXISTS (SELECT 1 FROM fn_workflow_recovery_task AS existing "
        + "WHERE existing.OpenOccupancyKey=" + OccupancyKey(mysql, steps)
        + " AND existing.StatusKey IN ('pending','failed','dead_lettered')) "
        + "ORDER BY instance.Id" + (steps ? ",step.Id" : "") + (mysql ? " LIMIT @BatchSize" : "");

    // 与双库迁移中的生成列表达式一致，利用现有唯一索引查占用，避免逐候选扫描恢复任务表。
    private static string OccupancyKey(bool mysql, bool steps) => mysql
        ? "CONCAT(instance.TenantScopeKey,'|',HEX(instance.Id),'|',@KindKey,'|'," + (steps ? "HEX(step.Id)" : "'NONE'") + ")"
        : "CONCAT(instance.TenantScopeKey,N'|',CONVERT(nvarchar(36),instance.Id) COLLATE Latin1_General_100_BIN2,N'|',"
          + "CONVERT(nvarchar(32),@KindKey) COLLATE Latin1_General_100_BIN2,N'|',CONVERT(nvarchar(36),"
          + (steps ? "step.Id" : "CONVERT(uniqueidentifier,'00000000-0000-0000-0000-000000000000')")
          + ") COLLATE Latin1_General_100_BIN2)";

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

    public static readonly SqlStatement CountTasksForScopeSqlServer = new(
        "workflow.recovery.count_for_scope.sqlserver",
        $"""
        SELECT COUNT(*)
        FROM fn_workflow_recovery_task
        WHERE TenantScopeKey = @TenantScopeKey
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=fn_workflow_recovery_task.InstanceId
              AND instance.TenantScopeKey=@TenantScopeKey AND {WorkflowCollaborationListSql.AllowsSqlServer})
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CountTasksForScopeMySql = new(
        "workflow.recovery.count_for_scope.mysql",
        $"""
        SELECT COUNT(*)
        FROM fn_workflow_recovery_task
        WHERE TenantScopeKey = @TenantScopeKey
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=fn_workflow_recovery_task.InstanceId
              AND instance.TenantScopeKey=@TenantScopeKey AND {WorkflowCollaborationListSql.AllowsMySql})
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListTasksForScopeSqlServer = new(
        "workflow.recovery.list_for_scope.sql_server",
        $"""
        SELECT {TaskColumns}
        FROM fn_workflow_recovery_task
        WHERE TenantScopeKey = @TenantScopeKey
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=fn_workflow_recovery_task.InstanceId
              AND instance.TenantScopeKey=@TenantScopeKey AND {WorkflowCollaborationListSql.AllowsSqlServer})
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
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=fn_workflow_recovery_task.InstanceId
              AND instance.TenantScopeKey=@TenantScopeKey AND {WorkflowCollaborationListSql.AllowsMySql})
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
