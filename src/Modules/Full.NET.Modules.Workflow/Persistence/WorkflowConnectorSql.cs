using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>受控连接器的实例锁、分发租约与不可变尝试；所有普通入口都须由可信实例作用域锚定。</summary>
internal static partial class WorkflowConnectorSql
{
    private const string Projection = """
        activation.Id,activation.InstanceId,activation.TenantScopeKey,activation.NodeKey,activation.ContinuationNodeKey,
        activation.ConnectorKey,activation.ConnectorVersion,activation.ConnectorHash,activation.ModeKey,
        activation.InputJson,activation.InputHash,activation.InputFormRevision,activation.RequestJson,activation.RequestHash,
        activation.ParallelJoinId,activation.ParallelBranchKey,activation.StatusKey,activation.CurrentAttemptId,activation.SelectedAttemptId,
        activation.LeaseGeneration,activation.LeaseOwnerKey,activation.LeaseExpiresAtUtc,activation.AttemptCount,
        activation.ApplyAttemptCount,activation.LastErrorKey,activation.DueAtUtc,activation.CancelRequestedAtUtc,
        activation.Revision,activation.CreatedAtUtc,activation.CompletedAtUtc,activation.AppliedFormRevision
        """;
    private const string AttemptProjection = """
        execution.Id,execution.CallId,execution.TenantScopeKey,execution.LeaseGeneration,execution.LeaseOwnerKey,
        execution.RequestHash,execution.StatusKey,execution.OutcomeKey,execution.ResponseJson,execution.ResponseHash,
        execution.HttpStatusCode,execution.ErrorKey,execution.StartedAtUtc,execution.CompletedAtUtc
        """;

    public static readonly SqlStatement Find = new("workflow.connector.find", "SELECT " + Projection + """
         FROM fn_workflow_connector_call AS activation WHERE activation.Id=@Id AND activation.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindAttempt = new("workflow.connector_attempt.find", "SELECT " + AttemptProjection + """
         FROM fn_workflow_connector_attempt AS execution WHERE execution.Id=@Id AND execution.CallId=@CallId AND execution.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);

    public static readonly SqlStatement LockInstance = new("workflow.connector.lock_instance", """
        UPDATE fn_workflow_instance SET Revision=Revision
        WHERE Id=@Id AND TenantScopeKey=@TenantScopeKey AND Revision=@Revision
        """, SqlDataScope.Global);

    public static readonly SqlStatement InsertStep = new("workflow.connector.insert_step", """
        INSERT INTO fn_workflow_step
          (Id,InstanceId, ExecutionEpochId,NodeKey,NodeTypeKey,StatusKey,AssignedUserId,DueAtUtc,AttemptCount,Revision,
           ExecutionSequence,StartedAtUtc,CompletedAtUtc,ParallelJoinId,ParallelBranchKey)
        VALUES (@Id,@InstanceId, @ExecutionEpochId,@NodeKey,@NodeTypeKey,'active',NULL,NULL,0,1,
           @ExecutionSequence,@Now,NULL,@ParallelJoinId,@ParallelBranchKey)
        """, SqlDataScope.Global);
    public static readonly SqlStatement Insert = new("workflow.connector.insert", """
        INSERT INTO fn_workflow_connector_call
          (Id,InstanceId,TenantScopeKey,NodeKey,ContinuationNodeKey,ConnectorKey,ConnectorVersion,ConnectorHash,ModeKey,
           InputJson,InputHash,InputFormRevision,RequestJson,RequestHash,ParallelJoinId,ParallelBranchKey,StatusKey,
           LeaseGeneration,AttemptCount,ApplyAttemptCount,DueAtUtc,Revision,CreatedAtUtc)
        VALUES (@Id,@InstanceId,@TenantScopeKey,@NodeKey,@ContinuationNodeKey,@ConnectorKey,@ConnectorVersion,@ConnectorHash,@ModeKey,
           @InputJson,@InputHash,@InputFormRevision,@RequestJson,@RequestHash,@ParallelJoinId,@ParallelBranchKey,'pending',
           0,0,0,@Now,1,@Now)
        """, SqlDataScope.Global);

    private const string DispatchDue = """
         FROM fn_workflow_connector_call AS activation
         INNER JOIN fn_workflow_instance AS instance ON instance.Id=activation.InstanceId AND instance.TenantScopeKey=activation.TenantScopeKey
         INNER JOIN fn_workflow_step AS step ON step.Id=activation.Id AND step.InstanceId=instance.Id
         WHERE (activation.StatusKey IN ('pending','retryable') AND instance.StatusKey='active'
                AND step.StatusKey='active' AND activation.CancelRequestedAtUtc IS NULL AND activation.DueAtUtc<=@Now)
            OR (activation.StatusKey='dispatched' AND activation.LeaseExpiresAtUtc<=@Now)
         ORDER BY activation.DueAtUtc,activation.Id
        """;
    public static readonly SqlStatement ListDispatchSqlServer = new("workflow.connector.dispatch_due.sqlserver", "SELECT TOP (@Take) " + Projection + DispatchDue, SqlDataScope.Global);
    public static readonly SqlStatement ListDispatchMySql = new("workflow.connector.dispatch_due.mysql", "SELECT " + Projection + DispatchDue + " LIMIT @Take", SqlDataScope.Global);

    private const string ApplyDue = """
         FROM fn_workflow_connector_call AS activation
         INNER JOIN fn_workflow_instance AS instance ON instance.Id=activation.InstanceId AND instance.TenantScopeKey=activation.TenantScopeKey
         INNER JOIN fn_workflow_step AS step ON step.Id=activation.Id AND step.InstanceId=instance.Id
         WHERE instance.StatusKey='active' AND step.StatusKey='active' AND activation.CancelRequestedAtUtc IS NULL AND activation.ModeKey='sync'
           AND (activation.StatusKey='result_ready' OR (activation.StatusKey='applying' AND activation.LeaseExpiresAtUtc<=@Now))
         ORDER BY activation.DueAtUtc,activation.Id
        """;
    public static readonly SqlStatement ListApplySqlServer = new("workflow.connector.apply_due.sqlserver", "SELECT TOP (@Take) " + Projection + ApplyDue, SqlDataScope.Global);
    public static readonly SqlStatement ListApplyMySql = new("workflow.connector.apply_due.mysql", "SELECT " + Projection + ApplyDue + " LIMIT @Take", SqlDataScope.Global);

    public static readonly SqlStatement ClaimDispatch = new("workflow.connector.claim_dispatch", """
        UPDATE fn_workflow_connector_call SET StatusKey='dispatched',CurrentAttemptId=@AttemptId,
          LeaseOwnerKey=@Owner,LeaseExpiresAtUtc=@Expires,LeaseGeneration=LeaseGeneration+1,
          AttemptCount=AttemptCount+1,Revision=Revision+1,LastErrorKey=NULL
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND Revision=@Revision
          AND StatusKey IN ('pending','retryable') AND DueAtUtc<=@Now AND CancelRequestedAtUtc IS NULL AND AttemptCount<@MaximumAttempts
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId
            AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
          AND EXISTS (SELECT 1 FROM fn_workflow_step AS step WHERE step.Id=@Id AND step.InstanceId=@InstanceId AND step.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement InsertAttempt = new("workflow.connector_attempt.insert", """
        INSERT INTO fn_workflow_connector_attempt
          (Id,CallId,TenantScopeKey,LeaseGeneration,LeaseOwnerKey,RequestHash,StatusKey,StartedAtUtc)
        VALUES (@AttemptId,@Id,@TenantScopeKey,@Generation,@Owner,@RequestHash,'inflight',@Now)
        """, SqlDataScope.Global);
    public static readonly SqlStatement ReceiveAttempt = new("workflow.connector_attempt.receive", """
        UPDATE fn_workflow_connector_attempt SET StatusKey='received',OutcomeKey=@OutcomeKey,
          ResponseJson=@ResponseJson,ResponseHash=@ResponseHash,HttpStatusCode=@HttpStatusCode,ErrorKey=@ErrorKey,CompletedAtUtc=@Now
        WHERE Id=@AttemptId AND CallId=@Id AND TenantScopeKey=@TenantScopeKey AND LeaseGeneration=@Generation
          AND LeaseOwnerKey=@Owner AND RequestHash=@RequestHash AND StatusKey='inflight' AND CompletedAtUtc IS NULL
        """, SqlDataScope.Global);

    public static readonly SqlStatement CaptureResult = new("workflow.connector.capture_result", """
        UPDATE fn_workflow_connector_call SET StatusKey=CASE
            WHEN CancelRequestedAtUtc IS NOT NULL AND @OutcomeKey IN ('succeeded','rejected','failed') THEN 'cancelled'
            WHEN CancelRequestedAtUtc IS NOT NULL THEN 'outcome_unknown' ELSE @NextStatusKey END,
          SelectedAttemptId=CASE WHEN @OutcomeKey='succeeded' THEN @AttemptId ELSE NULL END,
          CompletedAtUtc=CASE WHEN CancelRequestedAtUtc IS NOT NULL AND @OutcomeKey IN ('succeeded','rejected','failed') THEN @Now ELSE CompletedAtUtc END,
          LeaseOwnerKey=NULL,LeaseExpiresAtUtc=NULL,LastErrorKey=@ErrorKey,DueAtUtc=@DueAtUtc,Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='dispatched'
          AND LeaseGeneration=@Generation AND LeaseOwnerKey=@Owner AND CurrentAttemptId=@AttemptId AND LeaseExpiresAtUtc>@Now
        """, SqlDataScope.Global);

    public static readonly SqlStatement ClaimApply = new("workflow.connector.claim_apply", """
        UPDATE fn_workflow_connector_call SET StatusKey='applying',LeaseOwnerKey=@Owner,LeaseExpiresAtUtc=@Expires,
          LeaseGeneration=LeaseGeneration+1,ApplyAttemptCount=ApplyAttemptCount+1,Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND Revision=@Revision AND CancelRequestedAtUtc IS NULL
          AND (StatusKey='result_ready' OR (StatusKey='applying' AND LeaseExpiresAtUtc<=@Now))
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId
            AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
          AND EXISTS (SELECT 1 FROM fn_workflow_step AS step WHERE step.Id=@Id AND step.InstanceId=@InstanceId AND step.StatusKey='active')
        """, SqlDataScope.Global);
    public static readonly SqlStatement FinishApply = new("workflow.connector.finish_apply", """
        UPDATE fn_workflow_connector_call SET StatusKey='applied',AppliedFormRevision=@FormRevision,CompletedAtUtc=@Now,
          LeaseOwnerKey=NULL,LeaseExpiresAtUtc=NULL,LastErrorKey=NULL,Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='applying'
          AND LeaseGeneration=@Generation AND LeaseOwnerKey=@Owner AND LeaseExpiresAtUtc>@Now
          AND SelectedAttemptId=@AttemptId AND CancelRequestedAtUtc IS NULL
        """, SqlDataScope.Global);
}

/// <summary>固定输入与部署绑定属于激活，分发或接纳租约变化不重写这些快照。</summary>
internal sealed record WorkflowConnectorCallRecord(Guid Id,Guid InstanceId,string TenantScopeKey,string NodeKey,string ContinuationNodeKey,
    string ConnectorKey,int ConnectorVersion,string ConnectorHash,string ModeKey,string InputJson,string InputHash,long InputFormRevision,
    string RequestJson,string RequestHash,Guid? ParallelJoinId,string? ParallelBranchKey,string StatusKey,Guid? CurrentAttemptId,Guid? SelectedAttemptId,
    long LeaseGeneration,string? LeaseOwnerKey,DateTimeOffset? LeaseExpiresAtUtc,int AttemptCount,int ApplyAttemptCount,string? LastErrorKey,
    DateTimeOffset DueAtUtc,DateTimeOffset? CancelRequestedAtUtc,long Revision,DateTimeOffset CreatedAtUtc,DateTimeOffset? CompletedAtUtc,long? AppliedFormRevision);
/// <summary>迟到响应只能补原尝试，不能以新租约身份覆盖已经选定的活动结果。</summary>
internal sealed record WorkflowConnectorAttemptRecord(Guid Id,Guid CallId,string TenantScopeKey,long LeaseGeneration,string LeaseOwnerKey,
    string RequestHash,string StatusKey,string? OutcomeKey,string? ResponseJson,string? ResponseHash,int? HttpStatusCode,string? ErrorKey,
    DateTimeOffset StartedAtUtc,DateTimeOffset? CompletedAtUtc);
