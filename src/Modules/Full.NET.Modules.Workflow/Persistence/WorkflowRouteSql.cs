using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>路由固定原输入和唯一后继；未知选择只有精确授权修复可以改变，普通恢复不重新求值。</summary>
internal static class WorkflowRouteSql
{
    private const string Projection="""
        route.Id,route.InstanceId,route.TenantScopeKey,route.ExecutionEpochId,route.DefinitionVersionId,route.NodeKey,
        route.InputJson,route.InputHash,route.InputFormRevision,route.PolicyHash,route.StatusKey,route.SelectedTargetKey,
        route.NextNodeKey,route.TargetEpochId,route.NextWorkId,route.ParallelJoinId,route.ParallelBranchKey,
        route.TargetParallelJoinId,route.TargetParallelBranchKey,route.Revision,route.CreatedAtUtc,route.AppliedAtUtc
        """;
    public static readonly SqlStatement Find=new("workflow.route_decision.find","SELECT "+Projection+"""
         FROM fn_workflow_route_decision AS route
         WHERE route.Id=@Id AND route.InstanceId=@InstanceId AND route.TenantScopeKey=@TenantScopeKey
        """,SqlDataScope.Global);
    public static readonly SqlStatement InsertStep=new("workflow.route_decision.insert_step","""
        INSERT INTO fn_workflow_step (Id,InstanceId,ExecutionEpochId,NodeKey,NodeTypeKey,StatusKey,AssignedUserId,
            DueAtUtc,AttemptCount,Revision,ExecutionSequence,StartedAtUtc,CompletedAtUtc,ParallelJoinId,ParallelBranchKey)
        SELECT @Id,instance.Id,epoch.Id,@NodeKey,'control.route',@StepStatusKey,NULL,NULL,0,1,@ExecutionSequence,@Now,
            CASE WHEN @StepStatusKey='completed' THEN @Now ELSE NULL END,@ParallelJoinId,@ParallelBranchKey
        FROM fn_workflow_instance AS instance
        INNER JOIN fn_workflow_execution_epoch AS epoch ON epoch.InstanceId=instance.Id AND epoch.TenantScopeKey=instance.TenantScopeKey
        WHERE instance.Id=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active'
          AND epoch.Id=@ExecutionEpochId AND epoch.StatusKey='active'
        """,SqlDataScope.Global);
    public static readonly SqlStatement Insert=new("workflow.route_decision.insert","""
        INSERT INTO fn_workflow_route_decision (Id,InstanceId,TenantScopeKey,ExecutionEpochId,DefinitionVersionId,NodeKey,
            InputJson,InputHash,InputFormRevision,PolicyHash,StatusKey,SelectedTargetKey,NextNodeKey,TargetEpochId,NextWorkId,
            ParallelJoinId,ParallelBranchKey,TargetParallelJoinId,TargetParallelBranchKey,Revision,CreatedAtUtc,AppliedAtUtc)
        SELECT step.Id,step.InstanceId,instance.TenantScopeKey,step.ExecutionEpochId,instance.DefinitionVersionId,step.NodeKey,
            @InputJson,@InputHash,@InputFormRevision,@PolicyHash,@StatusKey,@SelectedTargetKey,@NextNodeKey,NULL,@NextWorkId,
            step.ParallelJoinId,step.ParallelBranchKey,NULL,NULL,1,@Now,NULL
        FROM fn_workflow_step AS step INNER JOIN fn_workflow_instance AS instance ON instance.Id=step.InstanceId
        WHERE step.Id=@Id AND step.InstanceId=@InstanceId AND instance.TenantScopeKey=@TenantScopeKey
          AND step.ExecutionEpochId=@ExecutionEpochId AND step.NodeTypeKey='control.route' AND instance.StatusKey='active'
          AND instance.DefinitionVersionId=@DefinitionVersionId
        """,SqlDataScope.Global);
    public static readonly SqlStatement FreezeSelection=new("workflow.route_decision.freeze_selection","""
        UPDATE fn_workflow_route_decision SET StatusKey='ready',SelectedTargetKey=@SelectedTargetKey,NextNodeKey=@NextNodeKey,
            NextWorkId=@NextWorkId,Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND Revision=@Revision AND StatusKey='decision_unknown'
        """,SqlDataScope.Global);
    public static readonly SqlStatement CompleteStep=new("workflow.route_decision.complete_step","""
        UPDATE fn_workflow_step SET StatusKey='completed',CompletedAtUtc=@Now,Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND NodeTypeKey='control.route' AND StatusKey='active'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId
              AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """,SqlDataScope.Global);
    public static readonly SqlStatement BindContinuation=new("workflow.route_decision.bind_continuation","""
        UPDATE fn_workflow_route_decision SET TargetEpochId=@TargetEpochId,TargetParallelJoinId=@TargetParallelJoinId,
            TargetParallelBranchKey=@TargetParallelBranchKey,Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND Revision=@Revision
          AND StatusKey='ready' AND TargetEpochId IS NULL AND NextWorkId=@NextWorkId
          AND EXISTS (SELECT 1 FROM fn_workflow_execution_epoch AS epoch WHERE epoch.Id=@TargetEpochId
              AND epoch.InstanceId=@InstanceId AND epoch.TenantScopeKey=@TenantScopeKey AND epoch.StatusKey='active')
        """,SqlDataScope.Global);
    public static readonly SqlStatement MarkApplied=new("workflow.route_decision.mark_applied","""
        UPDATE fn_workflow_route_decision SET StatusKey='applied',AppliedAtUtc=@Now,Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND Revision=@Revision AND StatusKey='ready'
          AND TargetEpochId=@TargetEpochId AND NextWorkId=@NextWorkId
          AND EXISTS (SELECT 1 FROM fn_workflow_activity_work AS work WHERE work.Id=@NextWorkId AND work.InstanceId=@InstanceId
              AND work.TenantScopeKey=@TenantScopeKey AND work.RouteDecisionId=@Id AND work.SourceStepId=@Id
              AND work.ExecutionEpochId=@TargetEpochId AND work.NodeKey=@NextNodeKey AND work.CauseKindKey='region')
        """,SqlDataScope.Global);
    public static readonly SqlStatement CountUnknown=new("workflow.route_decision.count_unknown","""
        SELECT COUNT(*) FROM fn_workflow_route_decision
        WHERE InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND StatusKey='decision_unknown'
        """,SqlDataScope.Global);
    public static readonly SqlStatement HoldSelection=new("workflow.route_decision.hold_selection","""
        UPDATE fn_workflow_route_decision SET StatusKey='decision_unknown',SelectedTargetKey=NULL,NextNodeKey=NULL,
            NextWorkId=NULL,Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND TenantScopeKey=@TenantScopeKey AND Revision=@Revision
          AND StatusKey='ready' AND TargetEpochId IS NULL
        """,SqlDataScope.Global);
    public static readonly SqlStatement HoldStep=new("workflow.route_decision.hold_step","""
        UPDATE fn_workflow_step SET StatusKey='active',CompletedAtUtc=NULL,Revision=Revision+1
        WHERE Id=@Id AND InstanceId=@InstanceId AND NodeTypeKey='control.route' AND StatusKey='completed'
          AND EXISTS (SELECT 1 FROM fn_workflow_instance AS instance WHERE instance.Id=@InstanceId
              AND instance.TenantScopeKey=@TenantScopeKey AND instance.StatusKey='active')
        """,SqlDataScope.Global);
    public static readonly SqlStatement InsertWork=new("workflow.route_decision.insert_work","""
        INSERT INTO fn_workflow_activity_work (Id,InstanceId,ExecutionEpochId,TenantScopeKey,NodeKey,InputJson,InputHash,InputFormRevision,
            ExecutionSequence,ParallelJoinId,ParallelBranchKey,DueAtUtc,StatusKey,LeaseGeneration,AttemptCount,ResumeBarrierId,
            SourceStepId,CauseKindKey,RouteDecisionId)
        SELECT route.NextWorkId,route.InstanceId,route.TargetEpochId,route.TenantScopeKey,route.NextNodeKey,route.InputJson,route.InputHash,
            @InputFormRevision,@ExecutionSequence,route.TargetParallelJoinId,route.TargetParallelBranchKey,@DueAtUtc,'pending',0,0,NULL,
            route.Id,'region',route.Id
        FROM fn_workflow_route_decision AS route
        INNER JOIN fn_workflow_execution_epoch AS epoch ON epoch.Id=route.TargetEpochId AND epoch.InstanceId=route.InstanceId
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=route.InstanceId AND instance.DefinitionVersionId=route.DefinitionVersionId
        WHERE route.Id=@RouteDecisionId AND route.InstanceId=@InstanceId AND route.TenantScopeKey=@TenantScopeKey
          AND route.StatusKey='ready' AND route.NextWorkId=@Id AND route.TargetEpochId=@ExecutionEpochId
          AND route.Id=@SourceStepId AND route.NextNodeKey=@NodeKey AND route.InputHash=@InputHash AND @CauseKindKey='region'
          AND instance.StatusKey='active' AND epoch.StatusKey='active' AND epoch.TenantScopeKey=@TenantScopeKey
        """,SqlDataScope.Global);
    private const string WorkProofQuery="""
        SELECT COUNT(*) FROM fn_workflow_route_decision AS route
        INNER JOIN fn_workflow_step AS source ON source.Id=route.Id AND source.InstanceId=route.InstanceId AND source.ExecutionEpochId=route.ExecutionEpochId
        INNER JOIN fn_workflow_instance AS instance ON instance.Id=route.InstanceId AND instance.DefinitionVersionId=route.DefinitionVersionId
        INNER JOIN fn_workflow_execution_epoch AS epoch ON epoch.Id=route.TargetEpochId AND epoch.InstanceId=route.InstanceId
        WHERE route.Id=@RouteDecisionId AND route.InstanceId=@InstanceId AND route.TenantScopeKey=@TenantScopeKey
          AND route.StatusKey='applied' AND route.NextWorkId=@WorkId AND route.Id=@SourceStepId
          AND route.TargetEpochId=@ExecutionEpochId AND route.NextNodeKey=@NodeKey AND route.InputHash=@InputHash
          AND source.NodeTypeKey='control.route' AND source.StatusKey='completed' AND instance.StatusKey='active'
          AND epoch.StatusKey='active' AND epoch.TenantScopeKey=@TenantScopeKey AND @CauseKindKey='region'
          AND (route.TargetParallelJoinId=@ParallelJoinId OR (route.TargetParallelJoinId IS NULL AND @ParallelJoinId IS NULL))
          AND (route.TargetParallelBranchKey=@ParallelBranchKey OR (route.TargetParallelBranchKey IS NULL AND @ParallelBranchKey IS NULL))
        """;
    public static readonly SqlStatement CountWorkProof=new("workflow.route_decision.count_work_proof",WorkProofQuery,SqlDataScope.Global);
    public static readonly SqlStatement CountJoinSourceProof=new("workflow.route_decision.count_join_source_proof",WorkProofQuery+"""
          AND route.NextNodeKey=@JoinNodeKey
          AND EXISTS (SELECT 1 FROM fn_workflow_activity_work AS work
            WHERE work.Id=route.NextWorkId AND work.RouteDecisionId=route.Id AND work.SourceStepId=route.Id
              AND work.InstanceId=@InstanceId AND work.TenantScopeKey=@TenantScopeKey AND work.StatusKey='leased'
              AND work.ExecutionEpochId=@ExecutionEpochId AND work.NodeKey=@JoinNodeKey AND work.InputHash=@InputHash
              AND work.ParallelJoinId=@ParallelJoinId AND work.ParallelBranchKey=@ParallelBranchKey)
        """,SqlDataScope.Global);
    public static readonly SqlStatement FindResolution=new("workflow.route_resolution.find","""
        SELECT receipt.RequestHash,receipt.Reason FROM fn_workflow_route_resolution AS receipt
        WHERE receipt.RouteId=@RouteId AND receipt.InstanceId=@InstanceId AND receipt.TenantScopeKey=@TenantScopeKey
          AND receipt.ActorUserId=@ActorUserId AND receipt.IdempotencyKey=@IdempotencyKey
        """,SqlDataScope.Global);
    public static readonly SqlStatement InsertResolution=new("workflow.route_resolution.insert","""
        INSERT INTO fn_workflow_route_resolution (Id,RouteId,InstanceId,TenantScopeKey,ActorUserId,IdempotencyKey,RequestHash,Reason,CreatedAtUtc)
        SELECT @Id,route.Id,route.InstanceId,route.TenantScopeKey,@ActorUserId,@IdempotencyKey,@RequestHash,@Reason,@Now
        FROM fn_workflow_route_decision AS route WHERE route.Id=@RouteId AND route.InstanceId=@InstanceId
          AND route.TenantScopeKey=@TenantScopeKey AND route.StatusKey='applied'
        """,SqlDataScope.Global);
}

/// <summary>固定路由事实保留原代次及真实帧；跨代次后继必须匹配该条唯一收据。</summary>
internal sealed record WorkflowRouteDecisionRecord(Guid Id,Guid InstanceId,string TenantScopeKey,Guid ExecutionEpochId,Guid DefinitionVersionId,
    string NodeKey,string InputJson,string InputHash,long InputFormRevision,string PolicyHash,string StatusKey,string? SelectedTargetKey,
    string? NextNodeKey,Guid? TargetEpochId,Guid? NextWorkId,Guid? ParallelJoinId,string? ParallelBranchKey,
    Guid? TargetParallelJoinId,string? TargetParallelBranchKey,long Revision,DateTimeOffset CreatedAtUtc,DateTimeOffset? AppliedAtUtc);

internal sealed record WorkflowRouteResolutionRecord(string RequestHash,string Reason);
