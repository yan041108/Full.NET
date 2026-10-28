using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>候选与提交共用一次有界快照；写入方必须持有根锁，不能把页面旧候选作为授权。</summary>
internal sealed class WorkflowReturnScopeReader(IQueryExecutor query, IOptions<DatabaseOptions> options)
{
    public async Task<WorkflowReturnScopeState?> ReadAsync(WorkflowInstanceRecord instance, Guid sourceStepId, CancellationToken token)
    {
        var provider = options.Value.Provider;
        if (provider is not (DatabaseProvider.SqlServer or DatabaseProvider.MySql)) throw new InvalidOperationException("workflow.return_scope.provider_unsupported");
        var parameters = WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey),
            ("Take", WorkflowActivityScheduler.InstanceActivityBudget + 1));
        var steps = await query.QueryAsync<WorkflowReturnStepFact>(provider == DatabaseProvider.SqlServer ? WorkflowReturnScopeSql.ListStepsSqlServer : WorkflowReturnScopeSql.ListStepsMySql, parameters, token).ConfigureAwait(false);
        if (steps.Count > WorkflowActivityScheduler.InstanceActivityBudget || steps.Select(step => step.Id).Distinct().Count() != steps.Count) return null;
        var source = steps.SingleOrDefault(step => step.Id == sourceStepId);
        if (source is null || source.InstanceId != instance.Id || steps.Count > WorkflowActivityScheduler.InstanceActivityBudget ||
            !await WorkflowExecutionFrameScope.IsValidAsync(query, instance, source.ExecutionEpochId, source.ParallelJoinId, source.ParallelBranchKey, token).ConfigureAwait(false)) return null;
        var forks = await query.QueryAsync<WorkflowReturnForkFact>(provider == DatabaseProvider.SqlServer ? WorkflowReturnScopeSql.ListForksSqlServer : WorkflowReturnScopeSql.ListForksMySql, parameters, token).ConfigureAwait(false);
        var work = await query.QueryAsync<WorkflowReturnWorkFact>(provider == DatabaseProvider.SqlServer ? WorkflowReturnScopeSql.ListWorkSqlServer : WorkflowReturnScopeSql.ListWorkMySql, parameters, token).ConfigureAwait(false);
        if (forks.Count > WorkflowActivityScheduler.InstanceActivityBudget || work.Count > WorkflowActivityScheduler.InstanceActivityBudget) return null;
        var causes = await query.QueryAsync<WorkflowStepCauseRecord>(WorkflowCausalitySql.ListEdges, parameters, token).ConfigureAwait(false);
        var bindings = await query.QueryAsync<WorkflowActivationBindingRecord>(WorkflowActivationBindingSql.List,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey), ("ExecutionEpochId", source.ExecutionEpochId)), token).ConfigureAwait(false);
        var frames = new Dictionary<Guid, WorkflowRegionFrameRecord>(); var lineage = new HashSet<Guid>(); var cursor = source.ExecutionEpochId;
        for (var depth = 0; depth <= 8; depth++)
        {
            if (!lineage.Add(cursor)) return null;
            var epoch = await WorkflowExecutionFrameScope.ReadActiveEpochAsync(query, instance, cursor, token).ConfigureAwait(false);
            if (epoch is null) return null;
            if (cursor == instance.Id) break;
            var frame = await WorkflowExecutionFrameScope.ReadFrameAsync(query, instance, cursor, token).ConfigureAwait(false);
            if (frame is null || epoch.ParentEpochId is not { } parent) return null;
            frames.Add(cursor, frame); cursor = parent;
        }
        if (!lineage.Contains(instance.Id)) return null;
        // 每个fork的实际外框也必须有效，不能用伪造context拼出一条跨代次父链。
        var currentFork = source.ParallelJoinId; var visitedForks = new HashSet<Guid>();
        while (currentFork is { } forkId)
        {
            if (visitedForks.Count >= 8 || !visitedForks.Add(forkId)) return null;
            var candidates = forks.Where(fork => fork.Id == forkId).ToArray();
            if (candidates.Length != 1 || !lineage.Contains(candidates[0].ExecutionEpochId) ||
                !await WorkflowExecutionFrameScope.IsValidAsync(query, instance, candidates[0].ExecutionEpochId,
                    candidates[0].ParentParallelJoinId, candidates[0].ParentBranchKey, token).ConfigureAwait(false)) return null;
            currentFork = candidates[0].ParentParallelJoinId;
        }
        return new(steps, forks, work, causes, bindings, frames.GetValueOrDefault(source.ExecutionEpochId), frames, lineage);
    }
}

/// <summary>单次根事务或只读请求的事实，不跨请求缓存，也不从最新同名节点补齐。</summary>
internal sealed record WorkflowReturnScopeState(IReadOnlyList<WorkflowReturnStepFact> Steps, IReadOnlyList<WorkflowReturnForkFact> Forks,
    IReadOnlyList<WorkflowReturnWorkFact> Work, IReadOnlyList<WorkflowStepCauseRecord> Causes, IReadOnlyList<WorkflowActivationBindingRecord> Bindings, WorkflowRegionFrameRecord? ParentFrame = null, IReadOnlyDictionary<Guid, WorkflowRegionFrameRecord>? Frames = null, IReadOnlySet<Guid>? EpochLineage = null)
{
    public bool TrySelect(WorkflowInstanceRecord instance, WorkflowRuntimePlan plan, Guid source, Guid target, out WorkflowReturnScopeSelection? selected)
    {
        if (!WorkflowReturnScopePolicy.TrySelect(instance, plan, source, target, Steps, Forks, Causes, Bindings, out selected, Work)) return false;
        var sourceEpoch = Steps.Single(step => step.Id == source).ExecutionEpochId;
        if (selected!.ParentEpochId != sourceEpoch && (EpochLineage is null || !EpochLineage.Contains(selected.ParentEpochId)))
        { selected = null; return false; }
        // 已整体关闭的来源子Frame不再约束新轮次；存活父Frame仍不可被更大的新子区域吞掉。
        var frame = selected.ParentEpochId == sourceEpoch ? ParentFrame : Frames?.GetValueOrDefault(selected.ParentEpochId);
        if (selected.ParentEpochId != instance.Id && (frame is null || frame.ExecutionEpochId != selected.ParentEpochId ||
            !plan.TryDescribeFixedReturnRegion(frame.EntryNodeKey, frame.ExitNodeKey, out var parent) ||
            selected.Region.NodeKeys.Any(key => !parent!.NodeKeys.Contains(key, StringComparer.Ordinal))))
        { selected = null; return false; }
        return true;
    }
}
