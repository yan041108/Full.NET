using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>在原根事务中登记退回入出收据；代次、有效绑定与工作必须一起提交，存储不一致整笔回滚。</summary>
internal sealed class WorkflowReturnRoundCoordinator(IQueryExecutor query, ICommandExecutor commands, IIdGenerator ids)
{
    /// <summary>来源已完成 Return 状态转换，原目标已失效；只有固定版本及原目标摘要能建立新的尝试。</summary>
    public async Task<Result<Guid>> EnterAsync(WorkflowInstanceRecord instance, WorkflowRuntimePlan plan, Guid sourceStepId,
        Guid targetStepId, string targetNodeKey, string targetSnapshotHash, IReadOnlyDictionary<string, JsonElement> values,
        long formRevision, long sequence, DateTimeOffset now, CancellationToken token, WorkflowReturnScopeSelection? selected = null)
    {
        var source = await query.QuerySingleOrDefaultAsync<WorkflowRegionRouteSourceRecord>(WorkflowReturnRoundSql.FindStep,
            WorkflowSqlParameters.Create(("StepId", sourceStepId), ("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey)), token).ConfigureAwait(false);
        if (source is null || source.Id != sourceStepId || source.InstanceId != instance.Id || source.DefinitionVersionId != instance.DefinitionVersionId ||
            source.StatusKey != "returned" || !plan.TryGetReturnNode(source.NodeKey, out var sourceNode) || source.NodeTypeKey != sourceNode!.NodeTypeKey ||
            !plan.TryGetReturnNode(targetNodeKey, out _) ||
            targetStepId == Guid.Empty || !IsHash(targetSnapshotHash) || formRevision < 1 || sequence < 1 ||
            !plan.TryDescribeReturnRegion(targetNodeKey, source.NodeKey, out var region, out var whole) || whole && source.ParallelJoinId is not null && selected is null ||
            !TrySuccessor(plan, region!.ExitNodeKey, out var nextNode)) return Invalid<Guid>();
        var parentJoinId = selected is null ? source.ParallelJoinId : selected.ParallelJoinId;
        var parentBranchKey = selected is null ? source.ParallelBranchKey : selected.ParallelBranchKey;
        if (selected is not null && (RegionJson(selected.Region) != RegionJson(region!) ||
            selected.NextNodeKey != nextNode || !selected.ActiveStepIds.Contains(sourceStepId) || !selected.InvalidatedStepIds.Contains(targetStepId))) return Invalid<Guid>();
        var parent = await WorkflowExecutionFrameScope.ReadActiveEpochAsync(query, instance, selected?.ParentEpochId ?? source.ExecutionEpochId, token).ConfigureAwait(false);
        if (parent is null || parent.Depth >= 8 || !await WorkflowExecutionFrameScope.IsValidAsync(query, instance, parent.Id,
                parentJoinId, parentBranchKey, token).ConfigureAwait(false)) return Invalid<Guid>();
        if (parent.Id != instance.Id)
        {
            var bound = await WorkflowExecutionFrameScope.ReadFrameAsync(query, instance, parent.Id, token).ConfigureAwait(false);
            if (bound is null || !plan.TryDescribeFixedReturnRegion(bound.EntryNodeKey, bound.ExitNodeKey, out var parentRegion) ||
                region!.NodeKeys.Any(key => !parentRegion!.NodeKeys.Contains(key, StringComparer.Ordinal))) return Invalid<Guid>();
        }
        var count = await query.QuerySingleOrDefaultAsync<int>(WorkflowActivitySql.CountActivations,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id)), token).ConfigureAwait(false);
        if (await WorkflowExecutionBudgetPolicy.ReadRemainingAsync(instance, plan.HasSubprocess, count, query, token).ConfigureAwait(false) < 1) return Invalid<Guid>();
        var regionJson = RegionJson(region); var regionHash = Hash(regionJson);
        var regionKey = "return." + regionHash[..40];
        var scope = WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey),
            ("ParentEpochId", parent.Id), ("RegionKey", regionKey));
        if (await query.QuerySingleOrDefaultAsync<int>(WorkflowRegionSql.CountActiveRegion, scope, token).ConfigureAwait(false) != 0) return Invalid<Guid>();
        var iteration = await query.QuerySingleOrDefaultAsync<int>(WorkflowRegionSql.NextIteration, scope, token).ConfigureAwait(false);
        if (iteration is < 1 or > 32) return Invalid<Guid>();
        var roundId = ids.NewId(); var workId = ids.NewId();
        if (roundId == Guid.Empty || workId == Guid.Empty || roundId == instance.Id || roundId == workId) throw Conflict();
        var input = WorkflowActivityScheduler.SerializeInput(values); var inputHash = Hash(input);
        var parameters = WorkflowSqlParameters.Create(("ExecutionEpochId", roundId), ("ReturnRoundId", roundId), ("InstanceId", instance.Id),
            ("TenantScopeKey", instance.TenantScopeKey), ("DefinitionVersionId", instance.DefinitionVersionId), ("ParentEpochId", parent.Id),
            ("CauseStepId", sourceStepId), ("SourceStepId", sourceStepId), ("EntrySourceEpochId", source.ExecutionEpochId), ("TargetStepId", targetStepId), ("RegionKey", regionKey),
            ("IterationNumber", iteration), ("Depth", parent.Depth + 1), ("Now", now), ("EntryNodeKey", region.EntryNodeKey),
            ("ExitNodeKey", region.ExitNodeKey), ("NextNodeKey", nextNode), ("RegionNodesJson", regionJson), ("RegionHash", regionHash),
            ("TargetSnapshotHash", targetSnapshotHash), ("ParentForkActivationId", parentJoinId), ("ParentBranchKey", parentBranchKey),
            ("ParallelJoinId", parentJoinId), ("ParallelBranchKey", parentBranchKey),
            ("SourceParallelJoinId", source.ParallelJoinId), ("SourceParallelBranchKey", source.ParallelBranchKey),
            ("EntryWorkId", workId), ("WorkId", workId), ("NodeKey", targetNodeKey), ("InputJson", input), ("InputHash", inputHash),
            ("InputFormRevision", formRevision), ("ExecutionSequence", sequence));
        if (await commands.ExecuteAsync(WorkflowRegionSql.InsertEpoch, parameters, token).ConfigureAwait(false) != 1 ||
            await commands.ExecuteAsync(WorkflowRegionSql.InsertFrame, parameters, token).ConfigureAwait(false) != 1) throw Conflict();
        await TransferBindingsAsync(instance, parent.Id, roundId, region.NodeKeys, false, now, token).ConfigureAwait(false);
        if (await commands.ExecuteAsync(WorkflowReturnRoundSql.Insert, parameters, token).ConfigureAwait(false) != 1 ||
            await commands.ExecuteAsync(WorkflowReturnRoundSql.InsertWork, parameters, token).ConfigureAwait(false) != 1) throw Conflict();
        await LogAsync(instance, sourceStepId, roundId, "todo.return.round.entered", now, token).ConfigureAwait(false);
        return Result<Guid>.Success(roundId);
    }

    /// <summary>在任何后继写入前完成实际出口；没有退回轮次或尚未到出口时返回空，失败不伪装成普通继续。</summary>
    public async Task<Result<WorkflowTransitionExecutionResult>?> TryExitAsync(WorkflowInstanceRecord instance, Guid epochId,
        WorkflowRuntimePlan plan, WorkflowActivationTrace trace, WorkflowDispatchBudget budget, Guid? joinId, string? branchKey,
        IReadOnlyDictionary<string, JsonElement> values, long sequence, DateTimeOffset now, CancellationToken token)
    {
        if (epochId == instance.Id || trace.Parents.Count != 1) return null;
        var round = await query.QuerySingleOrDefaultAsync<WorkflowReturnRoundRecord>(WorkflowReturnRoundSql.Find,
            WorkflowSqlParameters.Create(("ReturnRoundId", epochId), ("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey)), token).ConfigureAwait(false);
        if (round is null) return null;
        if (round.Id != epochId || round.InstanceId != instance.Id || round.TenantScopeKey != instance.TenantScopeKey ||
            round.DefinitionVersionId != instance.DefinitionVersionId || round.Revision != 1 || round.ExitWorkId is not null ||
            !IsHash(round.TargetSnapshotHash) ||
            !plan.TryDescribeFixedReturnRegion(round.EntryNodeKey, round.ExitNodeKey, out var region) ||
            round.RegionNodesJson != RegionJson(region!) || round.RegionHash != Hash(round.RegionNodesJson) ||
            !TrySuccessor(plan, round.ExitNodeKey, out var next) || next != round.NextNodeKey) return Invalid<WorkflowTransitionExecutionResult>();
        var source = await query.QuerySingleOrDefaultAsync<WorkflowRegionRouteSourceRecord>(WorkflowReturnRoundSql.FindStep,
            WorkflowSqlParameters.Create(("StepId", trace.LastStepId), ("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey)), token).ConfigureAwait(false);
        if (source is null || source.Id != trace.LastStepId || source.InstanceId != instance.Id || source.DefinitionVersionId != instance.DefinitionVersionId)
            return Invalid<WorkflowTransitionExecutionResult>();
        if (source.Id == round.SourceStepId && source.ExecutionEpochId == round.EntrySourceEpochId && source.StatusKey == "returned" &&
            trace.ReturnProof is { } entry && entry.RoundId == round.Id && entry.WorkId == round.EntryWorkId && entry.WorkId == budget.CurrentWorkId &&
            entry.NodeKey == round.EntryNodeKey && entry.InputHash == round.InputHash && entry.InputFormRevision == round.InputFormRevision)
            return null;
        if (source.NodeKey != round.ExitNodeKey) return null;
        if (source.StatusKey != "completed" || round.ParallelJoinId != joinId || round.ParallelBranchKey != branchKey ||
            source.ParallelJoinId != joinId || source.ParallelBranchKey != branchKey) return Invalid<WorkflowTransitionExecutionResult>();
        Guid? priorWorkId = null;
        if (source.ExecutionEpochId != epochId)
        {
            var proof = trace.ReturnProof;
            if (proof is null || proof.WorkId != budget.CurrentWorkId || proof.NodeKey != round.NextNodeKey ||
                await query.QuerySingleOrDefaultAsync<int>(WorkflowReturnRoundSql.CountWorkProof,
                    WorkflowSqlParameters.Create(("ReturnRoundId", proof.RoundId), ("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey),
                        ("ExecutionEpochId", epochId), ("SourceStepId", source.Id), ("ParallelJoinId", joinId), ("ParallelBranchKey", branchKey),
                        ("WorkId", proof.WorkId), ("NodeKey", proof.NodeKey), ("InputHash", proof.InputHash),
                        ("InputFormRevision", proof.InputFormRevision), ("CauseKindKey", "return")), token).ConfigureAwait(false) != 1)
                return Invalid<WorkflowTransitionExecutionResult>();
            priorWorkId = proof.WorkId;
        }
        var epoch = await WorkflowExecutionFrameScope.ReadActiveEpochAsync(query, instance, epochId, token).ConfigureAwait(false);
        var frame = await WorkflowExecutionFrameScope.ReadFrameAsync(query, instance, epochId, token).ConfigureAwait(false);
        var parent = await WorkflowExecutionFrameScope.ReadActiveEpochAsync(query, instance, round.ParentEpochId, token).ConfigureAwait(false);
        if (epoch is null || parent is null || epoch.ParentEpochId != parent.Id || epoch.Depth != parent.Depth + 1 ||
            epoch.CauseStepId != round.SourceStepId || epoch.RegionKey != "return." + round.RegionHash[..40] || frame is null ||
            frame.EntryNodeKey != round.EntryNodeKey || frame.ExitNodeKey != round.ExitNodeKey ||
            frame.ParentForkActivationId != joinId || frame.ParentBranchKey != branchKey) return Invalid<WorkflowTransitionExecutionResult>();
        var parameters = WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey),
            ("ExecutionEpochId", epochId), ("WorkId", budget.CurrentWorkId));
        if (await query.QuerySingleOrDefaultAsync<int>(WorkflowRegionSql.CountActiveChildren, parameters, token).ConfigureAwait(false) != 0 ||
            await query.QuerySingleOrDefaultAsync<int>(WorkflowRegionSql.CountUnsettled, parameters, token).ConfigureAwait(false) != 0)
            return Invalid<WorkflowTransitionExecutionResult>();
        var revision = await new WorkflowBranchStateCoordinator(query, commands).ReadRevisionAsync(instance, joinId, branchKey, token).ConfigureAwait(false);
        if (revision is null) return Invalid<WorkflowTransitionExecutionResult>();
        var input = WorkflowActivityScheduler.SerializeInput(values); var inputHash = Hash(input); var workId = ids.NewId();
        if (workId == Guid.Empty || workId == round.EntryWorkId) throw Conflict();
        await TransferBindingsAsync(instance, epochId, parent.Id, region!.NodeKeys, true, now, token).ConfigureAwait(false);
        if (await commands.ExecuteAsync(WorkflowReturnRoundSql.BindExit,
                WorkflowSqlParameters.Create(("ReturnRoundId", round.Id), ("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey),
                    ("ExitWorkId", workId), ("ExitStepId", source.Id), ("ExitSourceEpochId", source.ExecutionEpochId),
                    ("ExitInputHash", inputHash), ("ExitInputFormRevision", revision.Value), ("ParentReturnWorkId", priorWorkId),
                    ("ParentReturnRoundId", trace.ReturnProof?.RoundId), ("ParentInputHash", trace.ReturnProof?.InputHash),
                    ("ParentInputFormRevision", trace.ReturnProof?.InputFormRevision), ("Now", now)), token).ConfigureAwait(false) != 1 ||
            await commands.ExecuteAsync(WorkflowRegionSql.CloseEpoch,
                WorkflowSqlParameters.Create(("ExecutionEpochId", epoch.Id), ("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey),
                    ("RegionKey", epoch.RegionKey), ("Revision", epoch.Revision), ("Now", now)), token).ConfigureAwait(false) != 1) throw Conflict();
        if (await commands.ExecuteAsync(WorkflowReturnRoundSql.InsertWork,
                WorkflowSqlParameters.Create(("ReturnRoundId", round.Id), ("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey),
                    ("WorkId", workId), ("ExecutionEpochId", parent.Id), ("SourceStepId", source.Id), ("NodeKey", round.NextNodeKey),
                    ("ParallelJoinId", joinId), ("ParallelBranchKey", branchKey), ("InputJson", input), ("InputHash", inputHash),
                    ("InputFormRevision", revision.Value), ("ExecutionSequence", sequence), ("Now", now)), token).ConfigureAwait(false) != 1) throw Conflict();
        await LogAsync(instance, source.Id, round.Id, "todo.return.round.exited", now, token).ConfigureAwait(false);
        return Result<WorkflowTransitionExecutionResult>.Success(new(sequence, null, false));
    }

    private async Task TransferBindingsAsync(WorkflowInstanceRecord instance, Guid source, Guid target, IReadOnlyList<string> region,
        bool exporting, DateTimeOffset now, CancellationToken token)
    {
        var rows = await query.QueryAsync<WorkflowActivationBindingRecord>(WorkflowActivationBindingSql.List,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey), ("ExecutionEpochId", source)), token).ConfigureAwait(false);
        if (rows.Count > WorkflowDefinitionCompiler.MaximumNodes || rows.Select(row => row.NodeKey).Distinct(StringComparer.Ordinal).Count() != rows.Count ||
            rows.Any(row => row.InstanceId != instance.Id || row.TenantScopeKey != instance.TenantScopeKey || row.ExecutionEpochId != source ||
                row.StepId == Guid.Empty || row.SourceEpochId == Guid.Empty)) throw Conflict();
        if (exporting)
            foreach (var key in region)
                await commands.ExecuteAsync(WorkflowActivationBindingSql.Delete, WorkflowSqlParameters.Create(("InstanceId", instance.Id),
                    ("TenantScopeKey", instance.TenantScopeKey), ("ExecutionEpochId", target), ("NodeKey", key)), token).ConfigureAwait(false);
        foreach (var row in rows.Where(row => region.Contains(row.NodeKey, StringComparer.Ordinal) == exporting))
            if (await commands.ExecuteAsync(WorkflowActivationBindingSql.Insert,
                    WorkflowSqlParameters.Create(("Id", ids.NewId()), ("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey),
                        ("ExecutionEpochId", target), ("NodeKey", row.NodeKey), ("StepId", row.StepId), ("SourceEpochId", row.SourceEpochId), ("Now", now)), token).ConfigureAwait(false) != 1) throw Conflict();
    }

    private Task<int> LogAsync(WorkflowInstanceRecord instance, Guid source, Guid round, string transition, DateTimeOffset now, CancellationToken token) =>
        commands.ExecuteAsync(WorkflowSql.InsertExecutionLog, WorkflowSqlParameters.Create(("Id", ids.NewId()), ("InstanceId", instance.Id),
            ("StepId", source), ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),
            ("TransitionKey", transition), ("FromStatusKey", "active"), ("ToStatusKey", "active"), ("IdempotencyKey", null),
            ("Summary", round.ToString("D")), ("CreatedAtUtc", now)), token);
    private static string RegionJson(WorkflowStructuredRegion region) => JsonSerializer.Serialize(region.NodeKeys.ToArray(), WorkflowJsonSerializerContext.Default.StringArray);
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool IsHash(string value) => value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    /// <summary>本轮End先结束私有轮次，唯一外层End工作再完成根；不向正常DAG增加自环。</summary>
    private static bool TrySuccessor(WorkflowRuntimePlan plan, string exit, out string? next)
    {
        if (plan.IsTerminalNode(exit)) { next = exit; return true; }
        return plan.TryGetNormalSuccessor(exit, out next);
    }
    private static InvalidOperationException Conflict() => new("workflow.return_round.storage_conflict");
    private static Result<T> Invalid<T>() => Result<T>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
}
