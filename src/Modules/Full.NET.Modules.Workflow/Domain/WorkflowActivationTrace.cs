using Full.NET.Abstractions.Ids;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>单条推进路径的显式因果游标；分叉复制游标，不能与兄弟分支共享可变前驱。</summary>
internal sealed class WorkflowActivationTrace
{
    public WorkflowActivationTrace(Guid sourceStepId,Guid? parallelJoinId=null,string? branchKey=null,string kindKey="sequence",WorkflowRouteContinuationProof? routeProof=null,
        WorkflowReturnContinuationProof? returnProof=null)
    {
        if (sourceStepId==Guid.Empty || parallelJoinId==Guid.Empty || (parallelJoinId is null)!=(branchKey is null) || branchKey is not null && string.IsNullOrWhiteSpace(branchKey))
            throw new InvalidOperationException("workflow.causality.source_missing");
        if(!IsKnownKind(kindKey))throw new InvalidOperationException("workflow.causality.kind_unknown");
        if(routeProof is not null && (kindKey!="region" || routeProof.RouteId!=sourceStepId || routeProof.WorkId==Guid.Empty ||
            !WorkflowNodeKeyValidator.IsValid(routeProof.NodeKey) || routeProof.InputHash.Length!=64 || routeProof.InputHash.Any(value=>value is not (>= '0' and <= '9' or >= 'a' and <= 'f'))))
            throw new InvalidOperationException("workflow.route.continuation_invalid");
        if (returnProof is not null && (routeProof is not null || kindKey != "return" || returnProof.RoundId == Guid.Empty || returnProof.WorkId == Guid.Empty ||
            !WorkflowNodeKeyValidator.IsValid(returnProof.NodeKey) || returnProof.InputFormRevision < 1 || returnProof.InputHash.Length != 64 ||
            returnProof.InputHash.Any(value => value is not (>= '0' and <= '9' or >= 'a' and <= 'f'))))
            throw new InvalidOperationException("workflow.return_round.continuation_invalid");
        Parents=[sourceStepId]; ParallelJoinId=parallelJoinId; BranchKey=branchKey; KindKey=kindKey;
        RouteProof=routeProof;
        ReturnProof=returnProof;
    }
    public static bool IsKnownKind(string? kind)=>kind is "sequence" or "fork" or "join" or "region" or "return";
    public IReadOnlyList<Guid> Parents { get; private set; }
    public Guid? ParallelJoinId { get; private set; }
    public string? BranchKey { get; private set; }
    public string KindKey { get; private set; }="sequence";
    public WorkflowRouteContinuationProof? RouteProof { get; private set; }
    public WorkflowReturnContinuationProof? ReturnProof { get; private set; }
    public Guid LastStepId=>Parents.Count==1?Parents[0]:throw new InvalidOperationException("workflow.causality.multiple_sources");

    public WorkflowActivationTrace Fork(Guid joinId,string branchKey) => new(LastStepId,joinId,branchKey) { KindKey="fork" };

    /// <summary>汇合前驱必须来自已持久化的实际到达集合；缺少来源的旧到达不能伪造成完整汇合链。</summary>
    public void Join(IReadOnlyList<Guid?> sources,Guid? parentJoinId,string? parentBranchKey)
    {
        var maximum=Math.Max(WorkflowGatewayLimits.MaximumBranches("gateway.parallel"),WorkflowGatewayLimits.MaximumBranches("gateway.inclusive"));
        if (sources.Count<1 || sources.Count>maximum || sources.Any(id=>id is null || id==Guid.Empty) ||
            parentJoinId==Guid.Empty || (parentJoinId is null)!=(parentBranchKey is null) || parentBranchKey is not null && string.IsNullOrWhiteSpace(parentBranchKey))
            throw new InvalidOperationException("workflow.causality.arrival_missing");
        // 空分支可共同以实际fork步骤为前驱；到达仍逐分支固化，因果边按实际父步骤去重。
        Parents=sources.Select(id=>id!.Value).Distinct().ToArray(); KindKey="join"; ParallelJoinId=parentJoinId; BranchKey=parentBranchKey; RouteProof=null; ReturnProof=null;
    }

    public async Task RecordAsync(ICommandExecutor command,IIdGenerator ids,Guid instanceId,string scope,Guid stepId,
        DateTimeOffset now,CancellationToken token)
    {
        if (stepId==Guid.Empty || Parents.Contains(stepId)) throw new InvalidOperationException("workflow.causality.invalid_step");
        foreach (var parent in Parents)
            if (await command.ExecuteAsync(WorkflowCausalitySql.InsertEdge,WorkflowSqlParameters.Create(("Id",ids.NewId()),
                ("InstanceId",instanceId),("TenantScopeKey",scope),("StepId",stepId),("ParentStepId",parent),
                ("KindKey",KindKey),("Now",now)),token).ConfigureAwait(false)!=1)
                throw new InvalidOperationException("workflow.causality.edge_missing");
        Parents=[stepId]; KindKey="sequence"; RouteProof=null; ReturnProof=null;
    }
}

/// <summary>只供原后继Work直接到达汇合时复验；实际写出任何目标步骤后即清除，不能传给后续活动。</summary>
internal sealed record WorkflowRouteContinuationProof(Guid RouteId,Guid WorkId,string NodeKey,string InputHash);

/// <summary>退回跨代次续办只凭原收据；写出实际步骤或分叉/汇合后不得继续继承该证明。</summary>
internal sealed record WorkflowReturnContinuationProof(Guid RoundId, Guid WorkId, string NodeKey, string InputHash, long InputFormRevision);
