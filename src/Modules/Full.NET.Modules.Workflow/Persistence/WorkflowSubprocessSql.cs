using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>父子版本、执行预算及关联只读取Workflow自有事实；普通作用域不能引用其他租户资产。</summary>
internal static class WorkflowSubprocessSql
{
    private const string CallProjection = "callState.Id,callState.InstanceId,callState.TenantScopeKey,callState.NodeKey,callState.ContinuationNodeKey,callState.ChildDefinitionVersionId,callState.ChildFormVersionId,callState.DefinitionHash,callState.FormHash,callState.PolicyJson,callState.ParentInputJson,callState.InputFormRevision,callState.ParallelJoinId,callState.ParallelBranchKey,callState.Depth,callState.ChildActivityBudget,callState.StatusKey,callState.CancellationRequested,callState.ErrorCode,callState.Revision,callState.CreatedAtUtc,callState.UpdatedAtUtc";
    private const string ItemProjection = "Id,CallId,InstanceId,TenantScopeKey,ItemKey,SequenceNo,StarterUserId,InputJson,InputHash,ChildInstanceId,StatusKey,ResultJson,ResultHash,ChildRevision,Revision,CreatedAtUtc,UpdatedAtUtc";
    public static readonly SqlStatement FindAssets = new("workflow.subprocess.find_assets", """
        SELECT version.Id AS DefinitionVersionId,version.FormVersionId,version.CanonicalJson,formVersion.FormSchemaJson,
            version.BusinessTitleTemplate,definition.StatusKey AS DefinitionStatusKey
        FROM fn_workflow_definition_version AS version
        INNER JOIN fn_workflow_definition AS definition ON definition.Id=version.DefinitionId
        INNER JOIN fn_workflow_form_version AS formVersion ON formVersion.Id=version.FormVersionId
        INNER JOIN fn_workflow_form_definition AS formDefinition ON formDefinition.Id=formVersion.FormDefinitionId
        WHERE version.Id IN @DefinitionVersionIds AND definition.TenantScopeKey=@TenantScopeKey AND formDefinition.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement Find = new("workflow.subprocess.find", "SELECT " + CallProjection + """
         FROM fn_workflow_subprocess_call AS callState WHERE callState.Id=@Id AND callState.InstanceId=@InstanceId AND callState.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindAtNode = new("workflow.subprocess.find_at_node", "SELECT " + CallProjection + """
         FROM fn_workflow_subprocess_call AS callState WHERE callState.InstanceId=@InstanceId AND callState.TenantScopeKey=@TenantScopeKey AND callState.NodeKey=@NodeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindItem = new("workflow.subprocess_item.find", "SELECT " + ItemProjection + """
         FROM fn_workflow_subprocess_item WHERE Id=@Id AND CallId=@CallId AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement Items = new("workflow.subprocess_item.list", "SELECT " + ItemProjection + """
         FROM fn_workflow_subprocess_item WHERE CallId=@CallId AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey ORDER BY SequenceNo,Id
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindBudget = new("workflow.execution_budget.find", """
        SELECT budget.InstanceId,budget.Granted,budget.Reserved,budget.Revision FROM fn_workflow_exec_budget AS budget
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=budget.InstanceId
        WHERE budget.InstanceId=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement InsertBudget = new("workflow.execution_budget.insert", """
        INSERT INTO fn_workflow_exec_budget (InstanceId,Granted,Reserved,Revision)
        SELECT Id,@Granted,0,1 FROM fn_workflow_instance WHERE Id=@InstanceId AND TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement ReserveBudget = new("workflow.execution_budget.reserve", """
        UPDATE fn_workflow_exec_budget SET Reserved=Reserved+@Reserve,Revision=Revision+1
        WHERE InstanceId=@InstanceId AND Revision=@Revision AND Granted-Reserved>=@Reserve+@UsedCount+@KeepRemaining
        AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey)
        """, SqlDataScope.Global);
    public static readonly SqlStatement FindDepth = new("workflow.subprocess.find_depth", """
        SELECT MAX(callState.Depth) FROM fn_workflow_subprocess_item AS item
        INNER JOIN fn_workflow_subprocess_call AS callState ON callState.Id=item.CallId AND callState.InstanceId=item.InstanceId
        WHERE item.ChildInstanceId=@InstanceId AND item.TenantScopeKey=@TenantScopeKey AND callState.TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement ActiveStepRevision = new("workflow.subprocess.active_step_revision", """
        SELECT step.Revision FROM fn_workflow_step AS step INNER JOIN fn_workflow_instance AS instance ON instance.Id=step.InstanceId
        WHERE step.Id=@Id AND step.InstanceId=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey
        AND step.NodeTypeKey='process.subprocess' AND step.StatusKey='active'
        """, SqlDataScope.Global);
    public static readonly SqlStatement StepStatus = new("workflow.subprocess.step_status", """
        SELECT step.StatusKey FROM fn_workflow_step AS step INNER JOIN fn_workflow_instance AS instance ON instance.Id=step.InstanceId
        WHERE step.Id=@Id AND step.InstanceId=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND step.NodeTypeKey='process.subprocess'
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountChildUncertainEffects = new("workflow.subprocess.count_child_uncertain_effects", """
        SELECT COUNT(*) FROM fn_workflow_connector_call AS connector WHERE connector.InstanceId=@InstanceId AND connector.TenantScopeKey=@TenantScopeKey
        AND (connector.StatusKey='outcome_unknown' OR EXISTS (SELECT 1 FROM fn_workflow_connector_attempt AS attempt
            WHERE attempt.CallId=connector.Id AND attempt.TenantScopeKey=@TenantScopeKey AND (attempt.StatusKey='inflight' OR attempt.OutcomeKey='outcome_unknown')))
        """, SqlDataScope.Global);
    public static readonly SqlStatement InsertStep = new("workflow.subprocess.insert_step", """
        INSERT INTO fn_workflow_step (Id,InstanceId,NodeKey,NodeTypeKey,StatusKey,AssignedUserId,DueAtUtc,AttemptCount,
            Revision,ExecutionSequence,StartedAtUtc,CompletedAtUtc,ParallelJoinId,ParallelBranchKey)
        VALUES (@Id,@InstanceId,@NodeKey,'process.subprocess','active',NULL,NULL,0,1,@ExecutionSequence,@Now,NULL,@ParallelJoinId,@ParallelBranchKey)
        """, SqlDataScope.Global);
    public static readonly SqlStatement Insert = new("workflow.subprocess.insert", """
        INSERT INTO fn_workflow_subprocess_call (Id,InstanceId,TenantScopeKey,NodeKey,ContinuationNodeKey,ChildDefinitionVersionId,ChildFormVersionId,
            DefinitionHash,FormHash,PolicyJson,ParentInputJson,InputFormRevision,ParallelJoinId,ParallelBranchKey,Depth,ChildActivityBudget,StatusKey,
            CancellationRequested,ErrorCode,Revision,CreatedAtUtc,UpdatedAtUtc)
        VALUES (@Id,@InstanceId,@TenantScopeKey,@NodeKey,@ContinuationNodeKey,@ChildDefinitionVersionId,@ChildFormVersionId,
            @DefinitionHash,@FormHash,@PolicyJson,@ParentInputJson,@InputFormRevision,@ParallelJoinId,@ParallelBranchKey,@Depth,@ChildActivityBudget,'requested',0,NULL,1,@Now,@Now)
        """, SqlDataScope.Global);
    public static readonly SqlStatement InsertItem = new("workflow.subprocess_item.insert", """
        INSERT INTO fn_workflow_subprocess_item (Id,CallId,InstanceId,TenantScopeKey,ItemKey,SequenceNo,StarterUserId,InputJson,InputHash,
            ChildInstanceId,StatusKey,ResultJson,ResultHash,ChildRevision,Revision,CreatedAtUtc,UpdatedAtUtc)
        VALUES (@Id,@CallId,@InstanceId,@TenantScopeKey,@ItemKey,@SequenceNo,@StarterUserId,@InputJson,@InputHash,NULL,'pending',NULL,NULL,NULL,1,@Now,@Now)
        """, SqlDataScope.Global);
    public static readonly SqlStatement LinkChild = new("workflow.subprocess_item.link_child", """
        UPDATE fn_workflow_subprocess_item SET ChildInstanceId=@ChildInstanceId,StatusKey='running',Revision=Revision+1,UpdatedAtUtc=@Now
        WHERE Id=@Id AND CallId=@CallId AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND InputHash=@InputHash
        AND ChildInstanceId IS NULL AND StatusKey='pending' AND Revision=@Revision
        """, SqlDataScope.Global);
    public static readonly SqlStatement MarkRunning = new("workflow.subprocess.mark_running", """
        UPDATE fn_workflow_subprocess_call SET StatusKey='running',Revision=Revision+1,UpdatedAtUtc=@Now
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='requested' AND Revision=@Revision
        """, SqlDataScope.Global);
    public static readonly SqlStatement RecordItemResult = new("workflow.subprocess_item.record_result", """
        UPDATE fn_workflow_subprocess_item SET StatusKey=@StatusKey,ResultJson=@ResultJson,ResultHash=@ResultHash,ChildRevision=@ChildRevision,
            Revision=Revision+1,UpdatedAtUtc=@Now WHERE Id=@Id AND CallId=@CallId AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey
            AND Revision=@Revision AND StatusKey IN ('pending','running')
        """, SqlDataScope.Global);
    public static readonly SqlStatement SetStatus = new("workflow.subprocess.set_status", """
        UPDATE fn_workflow_subprocess_call SET StatusKey=@StatusKey,CancellationRequested=@CancellationRequested,ErrorCode=@ErrorCode,
            Revision=Revision+1,UpdatedAtUtc=@Now WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND Revision=@Revision
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountRecoverable = new("workflow.subprocess.count_recoverable", """
        SELECT COUNT(*) FROM fn_workflow_subprocess_call WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey
        AND StatusKey IN ('requested','running','result_ready') AND CancellationRequested=0
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountBlocked = new("workflow.subprocess.count_blocked", """
        SELECT COUNT(*) FROM fn_workflow_subprocess_call WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey
        AND (StatusKey='faulted' OR CancellationRequested=1 OR ErrorCode IS NOT NULL)
        """, SqlDataScope.Global);
    public static readonly SqlStatement CountUnsettled = new("workflow.subprocess.count_unsettled", """
        SELECT COUNT(*) FROM fn_workflow_subprocess_call AS callState
        LEFT JOIN fn_workflow_step AS step ON step.Id=callState.Id AND step.InstanceId=callState.InstanceId
        WHERE callState.InstanceId=@InstanceId AND callState.TenantScopeKey=@TenantScopeKey AND
        (step.Id IS NULL OR NOT ((callState.StatusKey='applied' AND step.StatusKey='completed') OR (callState.StatusKey='cancelled' AND step.StatusKey<>'active')))
        """, SqlDataScope.Global);
    private const string Scan = " FROM fn_workflow_subprocess_call AS callState INNER JOIN fn_workflow_instance AS parent ON parent.Id=callState.InstanceId AND parent.TenantScopeKey=callState.TenantScopeKey WHERE callState.StatusKey NOT IN ('applied','cancelled') AND (callState.StatusKey<>'faulted' OR parent.StatusKey IN ('rejected','cancelled')) AND (@HasAfter=0 OR callState.UpdatedAtUtc>@AfterUpdatedAtUtc OR (callState.UpdatedAtUtc=@AfterUpdatedAtUtc AND callState.Id>@AfterId)) ORDER BY callState.UpdatedAtUtc,callState.Id";
    public static readonly SqlStatement PendingSqlServer = new("workflow.subprocess.pending_sqlserver", "SELECT TOP (@Take) " + CallProjection + Scan, SqlDataScope.Global);
    public static readonly SqlStatement PendingMySql = new("workflow.subprocess.pending_mysql", "SELECT " + CallProjection + Scan + " LIMIT @Take", SqlDataScope.Global);
}

/// <summary>父活动的固定版本、输入及预算；结果始终归属于真实步骤身份。</summary>
internal sealed record WorkflowSubprocessCallRecord(Guid Id, Guid InstanceId, string TenantScopeKey, string NodeKey, string ContinuationNodeKey,
    Guid ChildDefinitionVersionId, Guid ChildFormVersionId, string DefinitionHash, string FormHash, string PolicyJson, string ParentInputJson,
    long InputFormRevision, Guid? ParallelJoinId, string? ParallelBranchKey, int Depth, int ChildActivityBudget, string StatusKey,
    bool CancellationRequested, string? ErrorCode, long Revision, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

/// <summary>每个子项拥有稳定身份与输入；创建子根时原子关联，不能依靠最近同名实例恢复。</summary>
internal sealed record WorkflowSubprocessItemRecord(Guid Id, Guid CallId, Guid InstanceId, string TenantScopeKey, string ItemKey, int SequenceNo,
    Guid StarterUserId, string InputJson, string InputHash, Guid? ChildInstanceId, string StatusKey, string? ResultJson, string? ResultHash,
    long? ChildRevision, long Revision, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

/// <summary>父节点按本根锁预留子额度，不退款的层级分配保证整组活动总额有界。</summary>
internal sealed record WorkflowExecutionBudgetRecord(Guid InstanceId, int Granted, int Reserved, long Revision);
