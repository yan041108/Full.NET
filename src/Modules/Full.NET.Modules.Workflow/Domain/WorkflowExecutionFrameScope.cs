using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>局部区域沿不可变外层帧授权；同根、同名分支或任意祖先身份都不能单独授予到达资格。</summary>
internal static class WorkflowExecutionFrameScope
{
    public static async Task<bool> IsValidAsync(IQueryExecutor query, WorkflowInstanceRecord instance, Guid executionEpochId,
        Guid? forkActivationId, string? branchKey, CancellationToken token)
    {
        if (instance.Id == Guid.Empty || instance.StatusKey != "active" || executionEpochId == Guid.Empty ||
            instance.TenantScopeKey != (instance.TenantId is { } tenant ? $"tenant:{tenant:N}" : "host") ||
            instance.ScopeKey != (instance.TenantId is null ? "host" : "tenant") ||
            (forkActivationId is null ? branchKey is not null : forkActivationId == Guid.Empty || !WorkflowNodeKeyValidator.IsValid(branchKey))) return false;
        var cursor = await ReadActiveEpochAsync(query, instance, executionEpochId, token).ConfigureAwait(false);
        if (cursor is null) return false;
        WorkflowParallelJoinRecord? fork = null;
        if (forkActivationId is { } id)
        {
            fork = await query.QuerySingleOrDefaultAsync<WorkflowParallelJoinRecord>(WorkflowSql.FindParallelJoinById,
                WorkflowSqlParameters.Create(("Id", id), ("InstanceId", instance.Id)), token).ConfigureAwait(false);
            if (fork is null || fork.Id != id || fork.InstanceId != instance.Id || fork.StatusKey != "waiting" || fork.ExecutionEpochId == Guid.Empty) return false;
        }
        var lineage = new List<(WorkflowExecutionEpochRecord Epoch, WorkflowRegionFrameRecord? Frame)>();
        for (var depth = 0; depth <= 8; depth++)
        {
            var frame = cursor.Id == instance.Id ? null : await ReadFrameAsync(query, instance, cursor.Id, token).ConfigureAwait(false);
            if (cursor.Id != instance.Id && frame is null) return false;
            lineage.Add((cursor, frame));
            if (cursor.Id == instance.Id) break;
            if (cursor.ParentEpochId is not { } parentId) return false;
            var parent = await ReadActiveEpochAsync(query, instance, parentId, token).ConfigureAwait(false);
            if (parent is null || parent.Depth != cursor.Depth - 1) return false;
            cursor = parent;
        }
        if (lineage.Count == 0 || lineage[^1].Epoch.Id != instance.Id) return false;
        if (fork is null) return lineage.All(item => item.Frame?.ParentForkActivationId is null && item.Frame?.ParentBranchKey is null);
        var enclosing = lineage.FindIndex(item => item.Epoch.Id == fork.ExecutionEpochId);
        if (enclosing < 0 || lineage.Take(enclosing).Any(item => item.Frame?.ParentForkActivationId != forkActivationId || item.Frame?.ParentBranchKey != branchKey)) return false;
        return await query.QuerySingleOrDefaultAsync<int>(WorkflowBranchSql.CountSelectedBranch,
            WorkflowSqlParameters.Create(("ParallelJoinId", fork.Id), ("BranchKey", branchKey)), token).ConfigureAwait(false) == 1;
    }

    internal static async Task<WorkflowExecutionEpochRecord?> ReadActiveEpochAsync(IQueryExecutor query, WorkflowInstanceRecord instance,
        Guid epochId, CancellationToken token)
    {
        var epoch = await query.QuerySingleOrDefaultAsync<WorkflowExecutionEpochRecord>(WorkflowExecutionEpochSql.Find,
            WorkflowSqlParameters.Create(("ExecutionEpochId", epochId), ("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey)), token).ConfigureAwait(false);
        if (epoch is null || epoch.Id != epochId || epoch.InstanceId != instance.Id || epoch.TenantScopeKey != instance.TenantScopeKey || epoch.StatusKey != "active" || epoch.ClosedAtUtc is not null || epoch.Revision < 1 || epoch.Depth is < 0 or > 8) return null;
        return epoch.Id == instance.Id
            ? epoch.ParentEpochId is null && epoch.CauseStepId is null && epoch.Depth == 0 && epoch.IterationNumber == 0 && epoch.RegionKey == "root" ? epoch : null
            : epoch.ParentEpochId is not null && epoch.CauseStepId is not null && epoch.Depth > 0 && epoch.IterationNumber is >= 1 and <= 32 &&
              epoch.RegionKey != "root" && epoch.RegionKey.Length <= 64 && WorkflowNodeKeyValidator.IsValid(epoch.RegionKey) ? epoch : null;
    }

    internal static async Task<WorkflowRegionFrameRecord?> ReadFrameAsync(IQueryExecutor query, WorkflowInstanceRecord instance,
        Guid epochId, CancellationToken token)
    {
        var frame = await query.QuerySingleOrDefaultAsync<WorkflowRegionFrameRecord>(WorkflowRegionSql.FindFrame,
            WorkflowSqlParameters.Create(("ExecutionEpochId", epochId), ("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey)), token).ConfigureAwait(false);
        return frame is not null && frame.ExecutionEpochId == epochId && frame.InstanceId == instance.Id &&
            frame.TenantScopeKey == instance.TenantScopeKey && frame.DefinitionVersionId == instance.DefinitionVersionId &&
            WorkflowNodeKeyValidator.IsValid(frame.EntryNodeKey) && WorkflowNodeKeyValidator.IsValid(frame.ExitNodeKey) && frame.EntryNodeKey != frame.ExitNodeKey &&
            (frame.ParentForkActivationId is null ? frame.ParentBranchKey is null : frame.ParentForkActivationId != Guid.Empty && WorkflowNodeKeyValidator.IsValid(frame.ParentBranchKey)) ? frame : null;
    }
}
