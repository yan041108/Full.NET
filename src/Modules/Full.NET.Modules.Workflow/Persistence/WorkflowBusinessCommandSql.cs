using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>业务等待只属于Workflow；根状态及真实步骤同属本模块，可在实例锁事务中共同裁决。</summary>
internal static class WorkflowBusinessCommandSql
{
    private const string Projection = "businessCall.Id,businessCall.InstanceId,businessCall.TenantScopeKey,businessCall.NodeKey,businessCall.ContinuationNodeKey,businessCall.ActionKey,businessCall.ActionVersion,businessCall.ActionHash,businessCall.BindingJson,businessCall.InputHash,businessCall.StatusKey,businessCall.CompensationRequested,businessCall.Generation,businessCall.OwnerInputHash,businessCall.AppliedVersion,businessCall.CompensatedVersion,businessCall.ErrorCode,businessCall.ResultJson,businessCall.Revision,businessCall.CreatedAtUtc,businessCall.UpdatedAtUtc";
    public static readonly SqlStatement Find = new("workflow.business_command.find", "SELECT " + Projection + """
         FROM fn_workflow_business_cmd AS businessCall
         WHERE businessCall.Id=@Id AND businessCall.InstanceId=@InstanceId AND businessCall.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindAtNode = new("workflow.business_command.find_at_node", "SELECT " + Projection + """
         FROM fn_workflow_business_cmd AS businessCall
         WHERE businessCall.InstanceId=@InstanceId AND businessCall.TenantScopeKey=@TenantScopeKey AND businessCall.NodeKey=@NodeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement ApprovedPredecessors = new("workflow.business_command.approved_predecessors", """
        SELECT COUNT(*) FROM fn_workflow_step AS step
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=step.InstanceId
        WHERE step.InstanceId=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey
          AND step.NodeKey IN @NodeKeys AND step.NodeTypeKey IN ('human.approval','human.group_approval')
          AND step.StatusKey='completed' AND step.ParallelJoinId IS NULL
        """, SqlDataScope.Global);
    public static readonly SqlStatement ActiveStepRevision = new("workflow.business_command.active_step_revision", """
        SELECT step.Revision FROM fn_workflow_step AS step
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=step.InstanceId
        WHERE step.Id=@Id AND step.InstanceId=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey
          AND step.StatusKey='active' AND step.NodeTypeKey='business.command' AND step.ParallelJoinId IS NULL
        """, SqlDataScope.Global);
    public static readonly SqlStatement InsertStep = new("workflow.business_command.insert_step", """
        INSERT INTO fn_workflow_step (Id,InstanceId,NodeKey,NodeTypeKey,StatusKey,AssignedUserId,DueAtUtc,
            AttemptCount,Revision,ExecutionSequence,StartedAtUtc,CompletedAtUtc,ParallelJoinId,ParallelBranchKey)
        VALUES (@Id,@InstanceId,@NodeKey,'business.command','active',NULL,NULL,0,1,@ExecutionSequence,@Now,NULL,NULL,NULL)
        """, SqlDataScope.Global);
    public static readonly SqlStatement Insert = new("workflow.business_command.insert", """
        INSERT INTO fn_workflow_business_cmd (Id,InstanceId,TenantScopeKey,NodeKey,ContinuationNodeKey,ActionKey,ActionVersion,ActionHash,
            BindingJson,InputHash,StatusKey,CompensationRequested,Generation,OwnerInputHash,AppliedVersion,CompensatedVersion,ErrorCode,ResultJson,Revision,CreatedAtUtc,UpdatedAtUtc)
        VALUES (@Id,@InstanceId,@TenantScopeKey,@NodeKey,@ContinuationNodeKey,@ActionKey,@ActionVersion,@ActionHash,
            @BindingJson,@InputHash,'requested',0,1,NULL,NULL,NULL,NULL,NULL,1,@Now,@Now)
        """, SqlDataScope.Global);
    public static readonly SqlStatement AcceptResult = new("workflow.business_command.accept_result", """
        UPDATE fn_workflow_business_cmd SET StatusKey=@StatusKey,OwnerInputHash=@OwnerInputHash,AppliedVersion=@AppliedVersion,
            CompensatedVersion=@CompensatedVersion,ErrorCode=@ErrorCode,ResultJson=@ResultJson,Revision=Revision+1,UpdatedAtUtc=@Now
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND InputHash=@InputHash
          AND Revision=@Revision AND Generation=@Generation
        """, SqlDataScope.Global);
    public static readonly SqlStatement BeginCompensation = new("workflow.business_command.begin_compensation", """
        UPDATE fn_workflow_business_cmd SET StatusKey='compensating',CompensationRequested=1,Generation=Generation+1,
            ErrorCode=NULL,ResultJson=NULL,Revision=Revision+1,UpdatedAtUtc=@Now
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND Revision=@Revision
          AND CompensationRequested=0 AND Generation<1000 AND StatusKey NOT IN ('cancelled','compensated','declined')
        """, SqlDataScope.Global);
    public static readonly SqlStatement Complete = new("workflow.business_command.complete", """
        UPDATE fn_workflow_business_cmd SET StatusKey='applied',Revision=Revision+1,UpdatedAtUtc=@Now
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND Revision=@Revision AND StatusKey='result_ready'
        """, SqlDataScope.Global);
    public static readonly SqlStatement Fault = new("workflow.business_command.fault", """
        UPDATE fn_workflow_business_cmd SET StatusKey='faulted',ErrorCode='workflow.business_command.binding_invalid',
            Revision=Revision+1,UpdatedAtUtc=@Now
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND Revision=@Revision
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountUnsettled = new("workflow.business_command.count_unsettled", """
        SELECT CASE WHEN COUNT(*)=1 AND SUM(CASE WHEN businessCall.StatusKey='applied' AND step.StatusKey='completed'
            AND businessCall.CompensationRequested=0 THEN 1 ELSE 0 END)=1 THEN 0 ELSE 1 END FROM fn_workflow_business_cmd AS businessCall
        INNER JOIN fn_workflow_step AS step ON step.Id=businessCall.Id AND step.InstanceId=businessCall.InstanceId
        WHERE businessCall.InstanceId=@InstanceId AND businessCall.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountRecoverable = new("workflow.business_command.count_recoverable", """
        SELECT COUNT(*) FROM fn_workflow_business_cmd WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey
        AND StatusKey IN ('requested','result_ready') AND CompensationRequested=0
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountBlocked = new("workflow.business_command.count_blocked", """
        SELECT COUNT(*) FROM fn_workflow_business_cmd WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey
        AND (StatusKey IN ('faulted','declined','compensating') OR CompensationRequested=1)
        """, SqlDataScope.Global);
    private const string Ready = " FROM fn_workflow_business_cmd AS businessCall INNER JOIN fn_workflow_instance AS instance ON instance.Id=businessCall.InstanceId AND instance.TenantScopeKey=businessCall.TenantScopeKey WHERE businessCall.StatusKey='result_ready' AND instance.StatusKey='active' AND (@HasAfter=0 OR businessCall.UpdatedAtUtc>@AfterUpdatedAtUtc OR (businessCall.UpdatedAtUtc=@AfterUpdatedAtUtc AND businessCall.Id>@AfterId)) ORDER BY businessCall.UpdatedAtUtc,businessCall.Id";
    private const string Compensation = " FROM fn_workflow_business_cmd AS businessCall INNER JOIN fn_workflow_instance AS instance ON instance.Id=businessCall.InstanceId AND instance.TenantScopeKey=businessCall.TenantScopeKey WHERE instance.StatusKey IN ('rejected','cancelled') AND businessCall.CompensationRequested=0 AND businessCall.StatusKey NOT IN ('cancelled','compensated','declined') AND NOT (businessCall.StatusKey='faulted' AND COALESCE(businessCall.ErrorCode,'')='workflow.business_command.binding_invalid') AND (@HasAfter=0 OR businessCall.UpdatedAtUtc>@AfterUpdatedAtUtc OR (businessCall.UpdatedAtUtc=@AfterUpdatedAtUtc AND businessCall.Id>@AfterId)) ORDER BY businessCall.UpdatedAtUtc,businessCall.Id";
    public static readonly SqlStatement ReadySqlServer = new("workflow.business_command.ready_sqlserver", "SELECT TOP (@Take) " + Projection + Ready, SqlDataScope.Global);
    public static readonly SqlStatement ReadyMySql = new("workflow.business_command.ready_mysql", "SELECT " + Projection + Ready + " LIMIT @Take", SqlDataScope.Global);
    public static readonly SqlStatement CompensationsSqlServer = new("workflow.business_command.compensations_sqlserver", "SELECT TOP (@Take) " + Projection + Compensation, SqlDataScope.Global);
    public static readonly SqlStatement CompensationsMySql = new("workflow.business_command.compensations_mysql", "SELECT " + Projection + Compensation + " LIMIT @Take", SqlDataScope.Global);
}

/// <summary>每次节点激活占用真实步骤身份；代次仅区分投递与补偿，不能变更原业务输入摘要。</summary>
internal sealed record WorkflowBusinessCommandRecord(Guid Id, Guid InstanceId, string TenantScopeKey, string NodeKey,
    string ContinuationNodeKey, string ActionKey, int ActionVersion, string ActionHash, string BindingJson, string InputHash,
    string StatusKey, bool CompensationRequested, int Generation, string? OwnerInputHash, long? AppliedVersion,
    long? CompensatedVersion, string? ErrorCode, string? ResultJson, long Revision, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
