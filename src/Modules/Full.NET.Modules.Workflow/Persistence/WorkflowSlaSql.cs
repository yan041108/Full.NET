using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>截止事实属于真实步骤；分组系统通过关闭未投票席位，不增加个人票数。</summary>
internal static class WorkflowSlaSql
{
    private const string Projection = """
        sla.Id, sla.InstanceId, sla.TenantScopeKey, sla.ActionKey, sla.PauseModeKey,
        sla.DueAtUtc, sla.RemainingMilliseconds, sla.PausedAtUtc, sla.StatusKey, sla.Revision,
        sla.CreatedAtUtc, sla.CompletedAtUtc, step.NodeKey, step.NodeTypeKey, step.Revision AS StepRevision,
        step.ParallelJoinId, step.ParallelBranchKey, step.StatusKey AS StepStatusKey
        """;
    private const string Due = """
        FROM fn_workflow_sla AS sla
        INNER JOIN fn_workflow_step AS step ON step.Id=sla.Id AND step.InstanceId=sla.InstanceId
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=sla.InstanceId AND instance.TenantScopeKey=sla.TenantScopeKey
        WHERE sla.StatusKey='waiting' AND (step.StatusKey<>'active' OR instance.StatusKey NOT IN ('active','suspended')
            OR (instance.StatusKey='active' AND sla.PausedAtUtc IS NULL AND sla.DueAtUtc<=@Now))
          AND (@HasAfter=0 OR sla.DueAtUtc>@AfterDueAtUtc OR (sla.DueAtUtc=@AfterDueAtUtc AND sla.Id>@AfterId))
        ORDER BY sla.DueAtUtc,sla.Id
        """;
    public static readonly SqlStatement ListDueSqlServer = new("workflow.sla.list_due.sqlserver", "SELECT TOP (@Take) " + Projection + " " + Due, SqlDataScope.Global);
    public static readonly SqlStatement ListDueMySql = new("workflow.sla.list_due.mysql", "SELECT " + Projection + " " + Due + " LIMIT @Take", SqlDataScope.Global);
    public static readonly SqlStatement Find = new("workflow.sla.find", "SELECT " + Projection + " " + """
        FROM fn_workflow_sla AS sla INNER JOIN fn_workflow_step AS step ON step.Id=sla.Id AND step.InstanceId=sla.InstanceId
        WHERE sla.Id=@Id AND sla.InstanceId=@InstanceId AND sla.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    private const string Active = """
        FROM fn_workflow_sla AS sla INNER JOIN fn_workflow_step AS step ON step.Id=sla.Id AND step.InstanceId=sla.InstanceId
        WHERE sla.InstanceId=@InstanceId AND sla.TenantScopeKey=@TenantScopeKey AND sla.StatusKey='waiting' AND step.StatusKey='active'
        ORDER BY sla.Id
        """;
    public static readonly SqlStatement ListActiveSqlServer = new("workflow.sla.list_active.sqlserver", "SELECT TOP (@Take) " + Projection + " " + Active, SqlDataScope.Global);
    public static readonly SqlStatement ListActiveMySql = new("workflow.sla.list_active.mysql", "SELECT " + Projection + " " + Active + " LIMIT @Take", SqlDataScope.Global);
    public static readonly SqlStatement Insert = new("workflow.sla.insert", """
        INSERT INTO fn_workflow_sla (Id,InstanceId,TenantScopeKey,ActionKey,PauseModeKey,DueAtUtc,RemainingMilliseconds,PausedAtUtc,StatusKey,Revision,CreatedAtUtc,CompletedAtUtc)
        SELECT @Id,@InstanceId,@TenantScopeKey,@ActionKey,@PauseModeKey,@DueAtUtc,NULL,NULL,'waiting',1,@Now,NULL
        FROM fn_workflow_instance WHERE Id=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='active'
        """, SqlDataScope.Global);
    public static readonly SqlStatement Finish = new("workflow.sla.finish", """
        UPDATE fn_workflow_sla SET StatusKey=@StatusKey,CompletedAtUtc=@Now,Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='waiting' AND Revision=@Revision
        """, SqlDataScope.Global);
    public static readonly SqlStatement Pause = new("workflow.sla.pause", """
        UPDATE fn_workflow_sla SET PausedAtUtc=@Now,RemainingMilliseconds=@RemainingMilliseconds,Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='waiting'
          AND PauseModeKey='freeze' AND PausedAtUtc IS NULL AND Revision=@Revision
        """, SqlDataScope.Global);
    public static readonly SqlStatement Resume = new("workflow.sla.resume", """
        UPDATE fn_workflow_sla SET PausedAtUtc=NULL,RemainingMilliseconds=NULL,DueAtUtc=@DueAtUtc,Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='waiting'
          AND PauseModeKey='freeze' AND PausedAtUtc IS NOT NULL AND Revision=@Revision
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelInstance = new("workflow.sla.cancel_instance", """
        UPDATE fn_workflow_sla SET StatusKey='cancelled',CompletedAtUtc=@Now,Revision=Revision+1
        WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='waiting'
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelStepTodos = new("workflow.sla.cancel_step_todos", """
        UPDATE fn_workflow_todo SET StatusKey='cancelled',ResultActionKey='cancelled',CompletedAtUtc=@Now,Revision=Revision+1
        WHERE InstanceId=@InstanceId AND StepId=@Id AND StatusKey IN ('active','suspended')
          AND EXISTS (SELECT 1 FROM fn_workflow_instance WHERE Id=@InstanceId AND TenantScopeKey=@TenantScopeKey)
        """, SqlDataScope.Global);
    public static readonly SqlStatement CancelStepSlots = new("workflow.sla.cancel_step_slots", """
        UPDATE fn_workflow_approval_slot SET DecisionKey='cancelled',DecidedAtUtc=@Now,Revision=Revision+1
        WHERE InstanceId=@InstanceId AND StepId=@Id AND DecisionKey IS NULL
          AND EXISTS (SELECT 1 FROM fn_workflow_instance WHERE Id=@InstanceId AND TenantScopeKey=@TenantScopeKey)
        """, SqlDataScope.Global);
}

/// <summary>显式步骤投影用于锁内重读；候选快照不能单独授权系统处置。</summary>
internal sealed record WorkflowSlaRecord(Guid Id, Guid InstanceId, string TenantScopeKey, string ActionKey, string PauseModeKey,
    DateTimeOffset DueAtUtc, long? RemainingMilliseconds, DateTimeOffset? PausedAtUtc, string StatusKey, long Revision,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? CompletedAtUtc, string NodeKey, string NodeTypeKey, long StepRevision,
    Guid? ParallelJoinId, string? ParallelBranchKey, string StepStatusKey);
