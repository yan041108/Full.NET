using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Modules.Workflow.Serialization;
using Full.NET.Abstractions.Ids;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>调用方持有真实根锁；所有准入先完成，开始写入后任何存储不一致都抛异常以回滚整个事务。</summary>
internal sealed class WorkflowRegionEpochCoordinator(IQueryExecutor query, ICommandExecutor commands, IIdGenerator ids)
{
    public async Task<WorkflowRegionEpochChange> EnterAsync(WorkflowInstanceRecord instance, Guid sourceEpochId, Guid sourceStepId,
        WorkflowDynamicRoutePlan plan, WorkflowCompiledRouteTarget target, Guid? currentWorkId, DateTimeOffset now, CancellationToken token)
    {
        var source = await ReadSourceAsync(instance, sourceEpochId, sourceStepId, plan, token).ConfigureAwait(false);
        if (source is null || !target.Reenters || target.Region is not { } selected || selected.ExitNodeKey != source.NodeKey ||
            selected.EntryNodeKey != target.Target.NodeKey || !plan.Routes[source.NodeKey].Targets.Contains(target) || target.Target.MaximumIterations is < 1 or > 32) return Invalid();
        if (await query.QuerySingleOrDefaultAsync<int>(WorkflowRegionSql.CountSourceEntries,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey), ("CauseStepId", source.Id)), token).ConfigureAwait(false) != 0)
            return Denied("workflow.activity.region_source_already_applied");
        var cursor = await WorkflowExecutionFrameScope.ReadActiveEpochAsync(query, instance, sourceEpochId, token).ConfigureAwait(false);
        if (cursor is null) return Invalid();
        var closing = new List<WorkflowExecutionEpochRecord>();
        var selectedNodes = selected.NodeKeys.ToHashSet(StringComparer.Ordinal);
        for (var depth = 0; cursor.Id != instance.Id; depth++)
        {
            if (depth >= 8) return Invalid();
            var frame = await WorkflowExecutionFrameScope.ReadFrameAsync(query, instance, cursor.Id, token).ConfigureAwait(false);
            var bound = frame is null ? null : await BoundRegionAsync(instance, plan, cursor, frame, token).ConfigureAwait(false);
            if (frame is null || bound is null || frame.EntryNodeKey != bound.EntryNodeKey || frame.ExitNodeKey != bound.ExitNodeKey) return Invalid();
            var currentNodes = bound.NodeKeys.ToHashSet(StringComparer.Ordinal);
            if (selectedNodes.IsProperSubsetOf(currentNodes)) break;
            if (!currentNodes.IsSubsetOf(selectedNodes) || frame.ExitNodeKey != source.NodeKey ||
                frame.ParentForkActivationId != source.ParallelJoinId || frame.ParentBranchKey != source.ParallelBranchKey || cursor.ParentEpochId is not { } parentId) return Invalid();
            closing.Add(cursor);
            var parent = await WorkflowExecutionFrameScope.ReadActiveEpochAsync(query, instance, parentId, token).ConfigureAwait(false);
            if (parent is null || parent.Depth != cursor.Depth - 1) return Invalid();
            cursor = parent;
        }
        if (cursor.Depth >= 8) return Denied("workflow.activity.region_depth_exhausted");
        if (!await CanCloseAsync(instance, closing, currentWorkId, token).ConfigureAwait(false)) return Denied("workflow.activity.region_unsettled");
        var regionParameters = WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey),
            ("ParentEpochId", cursor.Id), ("RegionKey", target.Target.RegionKey));
        if (await query.QuerySingleOrDefaultAsync<int>(WorkflowRegionSql.CountActiveRegion, regionParameters, token).ConfigureAwait(false) !=
            closing.Count(row => row.ParentEpochId == cursor.Id && row.RegionKey == target.Target.RegionKey)) return Denied("workflow.activity.region_unsettled");
        var iteration = await query.QuerySingleOrDefaultAsync<int>(WorkflowRegionSql.NextIteration,
            regionParameters, token).ConfigureAwait(false);
        if (iteration < 1) return Invalid();
        if (iteration > target.Target.MaximumIterations || iteration > 32) return Denied("workflow.activity.region_iteration_exhausted");
        var epochId = ids.NewId();
        if (epochId == Guid.Empty || epochId == instance.Id) throw new InvalidOperationException("workflow.execution_epoch.invalid_generated_id");
        await CloseAsync(instance, closing, now, token).ConfigureAwait(false);
        var parameters = WorkflowSqlParameters.Create(("ExecutionEpochId", epochId), ("InstanceId", instance.Id),
            ("TenantScopeKey", instance.TenantScopeKey), ("DefinitionVersionId", instance.DefinitionVersionId),
            ("ParentEpochId", cursor.Id), ("CauseStepId", source.Id), ("RegionKey", target.Target.RegionKey),
            ("IterationNumber", iteration), ("Depth", cursor.Depth + 1), ("Now", now),
            ("EntryNodeKey", selected.EntryNodeKey), ("ExitNodeKey", selected.ExitNodeKey),
            ("ParentForkActivationId", source.ParallelJoinId), ("ParentBranchKey", source.ParallelBranchKey));
        if (await commands.ExecuteAsync(WorkflowRegionSql.InsertEpoch, parameters, token).ConfigureAwait(false) != 1 ||
            await commands.ExecuteAsync(WorkflowRegionSql.InsertFrame, parameters, token).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("workflow.execution_epoch.region_write_conflict");
        await TransferBindingsAsync(instance, sourceEpochId, epochId, selectedNodes, false, now, token).ConfigureAwait(false);
        return new(epochId, null);
    }

    public async Task<WorkflowRegionEpochChange> ExitAsync(WorkflowInstanceRecord instance, Guid sourceEpochId, Guid sourceStepId,
        WorkflowDynamicRoutePlan plan, string nextNodeKey, Guid? currentWorkId, DateTimeOffset now, CancellationToken token)
    {
        var source = await ReadSourceAsync(instance, sourceEpochId, sourceStepId, plan, token).ConfigureAwait(false);
        if (source is null) return Invalid();
        var route = plan.Routes[source.NodeKey];
        if (nextNodeKey != route.NormalNextNodeKey && !route.Targets.Any(target => !target.Reenters && target.Target.NodeKey == nextNodeKey)) return Invalid();
        var cursor = await WorkflowExecutionFrameScope.ReadActiveEpochAsync(query, instance, sourceEpochId, token).ConfigureAwait(false);
        if (cursor is null) return Invalid();
        var closing = new List<WorkflowExecutionEpochRecord>();
        var closingRegions = new Dictionary<Guid, WorkflowStructuredRegion>();
        for (var depth = 0; cursor.Id != instance.Id; depth++)
        {
            if (depth >= 8) return Invalid();
            var frame = await WorkflowExecutionFrameScope.ReadFrameAsync(query, instance, cursor.Id, token).ConfigureAwait(false);
            var bound = frame is null ? null : await BoundRegionAsync(instance, plan, cursor, frame, token).ConfigureAwait(false);
            if (frame is null || bound is null || frame.EntryNodeKey != bound.EntryNodeKey || frame.ExitNodeKey != bound.ExitNodeKey) return Invalid();
            if (frame.ExitNodeKey != source.NodeKey)
            {
                if (!bound.NodeKeys.Contains(nextNodeKey, StringComparer.Ordinal)) return Invalid();
                break;
            }
            if (frame.ParentForkActivationId != source.ParallelJoinId || frame.ParentBranchKey != source.ParallelBranchKey || cursor.ParentEpochId is not { } parentId) return Invalid();
            closing.Add(cursor); closingRegions.Add(cursor.Id, bound);
            var parent = await WorkflowExecutionFrameScope.ReadActiveEpochAsync(query, instance, parentId, token).ConfigureAwait(false);
            if (parent is null || parent.Depth != cursor.Depth - 1) return Invalid();
            cursor = parent;
        }
        if (!await CanCloseAsync(instance, closing, currentWorkId, token).ConfigureAwait(false)) return Denied("workflow.activity.region_unsettled");
        foreach (var row in closing)
            await TransferBindingsAsync(instance, row.Id, row.ParentEpochId!.Value,
                closingRegions[row.Id].NodeKeys.ToHashSet(StringComparer.Ordinal), true, now, token).ConfigureAwait(false);
        await CloseAsync(instance, closing, now, token).ConfigureAwait(false);
        return new(cursor.Id, null);
    }

    /// <summary>进入只继承区域外完成事实；退出先清除父区旧轮绑定，再导出本轮实际走过的区域节点。</summary>
    private async Task TransferBindingsAsync(WorkflowInstanceRecord instance, Guid sourceEpoch, Guid targetEpoch,
        IReadOnlySet<string> regionNodes, bool exporting, DateTimeOffset now, CancellationToken token)
    {
        var rows = await query.QueryAsync<WorkflowActivationBindingRecord>(WorkflowActivationBindingSql.List,
            WorkflowSqlParameters.Create(("InstanceId",instance.Id),("TenantScopeKey",instance.TenantScopeKey),("ExecutionEpochId",sourceEpoch)),token).ConfigureAwait(false);
        if (rows.Count>WorkflowDefinitionCompiler.MaximumNodes || rows.Select(row=>row.NodeKey).Distinct(StringComparer.Ordinal).Count()!=rows.Count ||
            rows.Any(row=>row.InstanceId!=instance.Id || row.TenantScopeKey!=instance.TenantScopeKey || row.ExecutionEpochId!=sourceEpoch ||
                row.StepId==Guid.Empty || row.SourceEpochId==Guid.Empty || string.IsNullOrWhiteSpace(row.NodeKey)))
            throw new InvalidOperationException("workflow.activation_binding.corrupt");
        if (exporting)
            foreach (var nodeKey in regionNodes)
                await commands.ExecuteAsync(WorkflowActivationBindingSql.Delete,
                    WorkflowSqlParameters.Create(("InstanceId",instance.Id),("TenantScopeKey",instance.TenantScopeKey),
                        ("ExecutionEpochId",targetEpoch),("NodeKey",nodeKey)),token).ConfigureAwait(false);
        foreach (var row in rows.Where(row=>regionNodes.Contains(row.NodeKey)==exporting))
            if (await commands.ExecuteAsync(WorkflowActivationBindingSql.Insert,
                WorkflowSqlParameters.Create(("Id",ids.NewId()),("InstanceId",instance.Id),("TenantScopeKey",instance.TenantScopeKey),
                    ("ExecutionEpochId",targetEpoch),("NodeKey",row.NodeKey),("StepId",row.StepId),("SourceEpochId",row.SourceEpochId),("Now",now)),token).ConfigureAwait(false)!=1)
                throw new InvalidOperationException("workflow.activation_binding.copy_conflict");
    }

    private async Task<WorkflowRegionRouteSourceRecord?> ReadSourceAsync(WorkflowInstanceRecord instance, Guid epochId, Guid stepId,
        WorkflowDynamicRoutePlan plan, CancellationToken token)
    {
        if (stepId == Guid.Empty || epochId == Guid.Empty || instance.StatusKey != "active") return null;
        var source = await query.QuerySingleOrDefaultAsync<WorkflowRegionRouteSourceRecord>(WorkflowRegionSql.FindRouteSource,
            WorkflowSqlParameters.Create(("StepId", stepId), ("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey)), token).ConfigureAwait(false);
        return source is not null && source.Id == stepId && source.InstanceId == instance.Id && source.ExecutionEpochId == epochId &&
            source.DefinitionVersionId == instance.DefinitionVersionId && source.NodeTypeKey == WorkflowDynamicRoutePolicy.NodeType && source.StatusKey == "completed" &&
            plan.Routes.ContainsKey(source.NodeKey) && await WorkflowExecutionFrameScope.IsValidAsync(query, instance, epochId,
                source.ParallelJoinId, source.ParallelBranchKey, token).ConfigureAwait(false) ? source : null;
    }

    private async Task<bool> CanCloseAsync(WorkflowInstanceRecord instance, IReadOnlyList<WorkflowExecutionEpochRecord> closing,
        Guid? currentWorkId, CancellationToken token)
    {
        for (var index = 0; index < closing.Count; index++)
        {
            var row = closing[index]; var parameters = WorkflowSqlParameters.Create(("InstanceId", instance.Id),
                ("TenantScopeKey", instance.TenantScopeKey), ("ExecutionEpochId", row.Id), ("WorkId", currentWorkId));
            var expectedChildren = index > 0 && closing[index - 1].ParentEpochId == row.Id ? 1 : 0;
            if (await query.QuerySingleOrDefaultAsync<int>(WorkflowRegionSql.CountActiveChildren, parameters, token).ConfigureAwait(false) != expectedChildren ||
                await query.QuerySingleOrDefaultAsync<int>(WorkflowRegionSql.CountUnsettled, parameters, token).ConfigureAwait(false) != 0) return false;
        }
        return true;
    }

    private async Task CloseAsync(WorkflowInstanceRecord instance, IReadOnlyList<WorkflowExecutionEpochRecord> closing, DateTimeOffset now, CancellationToken token)
    {
        foreach (var row in closing)
            if (await commands.ExecuteAsync(WorkflowRegionSql.CloseEpoch,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey),
                    ("ExecutionEpochId", row.Id), ("RegionKey", row.RegionKey), ("Revision", row.Revision), ("Now", now)), token).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("workflow.execution_epoch.region_close_conflict");
    }

    /// <summary>Route可嵌入已登记Return，但不能把Return父帧当作自己的可关闭回边。</summary>
    private async Task<WorkflowStructuredRegion?> BoundRegionAsync(WorkflowInstanceRecord instance, WorkflowDynamicRoutePlan plan,
        WorkflowExecutionEpochRecord epoch, WorkflowRegionFrameRecord frame, CancellationToken token)
    {
        var routeRegion = plan.Routes.Values.SelectMany(route => route.Targets).FirstOrDefault(target => target.Target.RegionKey == epoch.RegionKey)?.Region;
        if (routeRegion is not null) return routeRegion;
        if (!epoch.RegionKey.StartsWith("return.", StringComparison.Ordinal) || plan.Structure is null) return null;
        var round = await query.QuerySingleOrDefaultAsync<WorkflowReturnRoundRecord>(WorkflowReturnRoundSql.Find,
            WorkflowSqlParameters.Create(("ReturnRoundId", epoch.Id), ("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey)), token).ConfigureAwait(false);
        if (round is null || round.Id != epoch.Id || round.InstanceId != instance.Id || round.TenantScopeKey != instance.TenantScopeKey ||
            round.DefinitionVersionId != instance.DefinitionVersionId || round.ParentEpochId != epoch.ParentEpochId || round.SourceStepId != epoch.CauseStepId ||
            round.Revision != 1 || round.ExitWorkId is not null || round.RegionHash.Length != 64 || epoch.RegionKey != "return." + round.RegionHash[..40] ||
            frame.EntryNodeKey != round.EntryNodeKey || frame.ExitNodeKey != round.ExitNodeKey ||
            frame.ParentForkActivationId != round.ParallelJoinId || frame.ParentBranchKey != round.ParallelBranchKey ||
            !plan.Structure.TryDescribeRegion(round.EntryNodeKey, round.ExitNodeKey, out var region)) return null;
        var json = JsonSerializer.Serialize(region!.NodeKeys.ToArray(), WorkflowJsonSerializerContext.Default.StringArray);
        return json == round.RegionNodesJson && round.RegionHash == Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json))) ? region : null;
    }
    private static WorkflowRegionEpochChange Invalid() => Denied("workflow.activity.region_invalid");
    private static WorkflowRegionEpochChange Denied(string key) => new(null, key);
}

/// <summary>准入失败尚未写入；存储失败抛异常，调用方不能把部分换代当作可提交的普通失败。</summary>
internal sealed record WorkflowRegionEpochChange(Guid? ExecutionEpochId, string? FailureKey)
{
    public bool IsSuccess => ExecutionEpochId is not null && FailureKey is null;
}
