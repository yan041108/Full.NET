using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Persistence;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>在事务外解析本轮人工等待点及抄送来源；事务内只消费固定的受信快照。</summary>
internal sealed class WorkflowHumanActivityPreparation(WorkflowHumanActivityAssigneeCoordinator coordinator, IQueryExecutor query,
    WorkflowStarterSelectionPreparation starter)
{
    /// <summary>原入队身份只恢复目标快照；正常退出工作返回空，继续走正常后继的准备协议。</summary>
    public async Task<Result<WorkflowPreparedHumanActivities?>> PrepareReturnAsync(WorkflowInstanceRecord instance, WorkflowActivityWorkRecord work,
        WorkflowRuntimePlan plan, WorkflowRuntimeTransition transition, CancellationToken token)
    {
        var round = await query.QuerySingleOrDefaultAsync<WorkflowReturnRoundRecord>(WorkflowReturnRoundSql.Find,
            WorkflowSqlParameters.Create(("ReturnRoundId", work.ReturnRoundId), ("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey)), token).ConfigureAwait(false);
        if (round is null || round.Id != work.ReturnRoundId || round.InstanceId != instance.Id || round.TenantScopeKey != instance.TenantScopeKey ||
            round.DefinitionVersionId != instance.DefinitionVersionId) return ReturnFailure();
        if (round.ExitWorkId == work.Id) return Result<WorkflowPreparedHumanActivities?>.Success(null);
        if (round.EntryWorkId != work.Id || round.EntryNodeKey != work.NodeKey || work.ExecutionEpochId != round.Id ||
            transition.NextHumanNodeKey != work.NodeKey || transition.AutomaticNodes.Count != 0 || transition.ParallelFork is not null ||
            !plan.TryGetReturnNode(work.NodeKey, out var node)) return ReturnFailure();
        var snapshot = await query.QuerySingleOrDefaultAsync<WorkflowAssigneeSnapshotRecord>(WorkflowReturnRoundSql.FindTargetSnapshot,
            WorkflowSqlParameters.Create(("ReturnRoundId", round.Id), ("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey), ("WorkId", work.Id)), token).ConfigureAwait(false);
        if (snapshot is null || snapshot.AssigneeSnapshotHash != round.TargetSnapshotHash) return ReturnFailure();
        var restored = await coordinator.RestoreReturnAsync(node!.NodeTypeKey, node.Config, snapshot.AssigneeSnapshotJson, snapshot.AssigneeSnapshotHash,
            new(instance.TenantId, instance.ScopeKey, instance.TenantScopeKey), instance.StartedById, token).ConfigureAwait(false);
        if (!restored.IsSuccess) return ReturnFailure();
        return Result<WorkflowPreparedHumanActivities?>.Success(new(new Dictionary<string, Result<WorkflowHumanActivityAssignees>>(StringComparer.Ordinal)
            { [work.NodeKey] = restored }));

        static Result<WorkflowPreparedHumanActivities?> ReturnFailure() => Result<WorkflowPreparedHumanActivities?>.Failure(new(
            WorkflowErrorCodes.DefinitionAssigneePolicyInvalid, "The historical return assignees are unavailable.", ErrorType.BusinessRule));
    }

    public Task<Result<WorkflowStarterSelectionResolution>> PrepareStartSelectionsAsync(WorkflowDefinitionDraft draft,
        IReadOnlyDictionary<string, IReadOnlyList<Guid>>? selections, WorkflowManagementScope scope, CancellationToken token) =>
        starter.ResolveAsync(draft.Nodes.SelectMany(WorkflowDynamicAssigneeDefinitionRules.Sources).Where(source => source.StarterSelection is not null)
            .Select(source => source.StarterSelection!).ToArray(), selections, scope, token, true);

    public async Task<WorkflowPreparedHumanActivities> PrepareAsync(WorkflowRuntimeTransition transition,
        WorkflowManagementScope scope, Guid initiatorId, CancellationToken token,
        IReadOnlyDictionary<string, JsonElement>? values = null, Guid? instanceId = null, string? completingNodeKey = null,
        WorkflowStarterSelectionResolution? starterSelections = null, IReadOnlyList<Guid>? flowAdministratorUserIds = null, Guid? executionEpochId = null)
    {
        var snapshots = new Dictionary<string, Result<WorkflowHumanActivityAssignees>>(StringComparer.Ordinal);
        var ccSnapshots = new Dictionary<string, Result<WorkflowCcRecipients>>(StringComparer.Ordinal);
        var ccNodes = new Dictionary<string, WorkflowAutomaticRuntimeNode>(StringComparer.Ordinal);
        var waits = new List<WorkflowRuntimeTransition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Collect(WorkflowRuntimeTransition current)
        {
            foreach (var node in current.AutomaticNodes)
            {
                if (ccNodes.Count >= WorkflowActivityScheduler.DispatchActivityBudget) break;
                if (node.NodeTypeKey == "notify.cc" && node.RecipientPolicy is not null) ccNodes.TryAdd(node.NodeKey, node);
            }
            if (current.NextHumanNodeKey is { } nodeKey && seen.Add(nodeKey)) waits.Add(current);
            if (current.ParallelFork is not { } fork) return;
            foreach (var branch in fork.Branches)
                Collect(new(branch.NextHumanNodeKey, branch.CompletesInstance,
                    branch.AutomaticNodes, branch.TimeoutPolicy, branch.ApprovalPolicy,
                    branch.AssigneePolicy, branch.ParallelFork, branch.JoinArrival, branch.TaskPolicy, branch.VotePolicy, branch.ClaimPolicy,
                    branch.AutomaticWait, branch.TerminalNodeKey));
        }
        Collect(transition);
        var allSources = waits.SelectMany(Sources).Concat(ccNodes.Values.SelectMany(node => node.RecipientPolicy!.Sources)).ToArray();
        var previousKeys = allSources.Where(source => source.ResolverKindKey == WorkflowAssigneePolicy.PreviousActors)
            .Select(source => source.PreviousNodeKey!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        IReadOnlyDictionary<string, IReadOnlyList<WorkflowPreviousActorRecord>> previous = new Dictionary<string, IReadOnlyList<WorkflowPreviousActorRecord>>();
        if (instanceId is { } id && executionEpochId is { } epoch && epoch != Guid.Empty && previousKeys.Length is > 0 and <= WorkflowDefinitionCompiler.MaximumNodes)
        {
            var rows = await query.QueryAsync<WorkflowPreviousActorRecord>(WorkflowDynamicAssigneeSql.ListPreviousActors,
                WorkflowSqlParameters.Create(("InstanceId", id), ("TenantScopeKey", scope.TenantScopeKey), ("ExecutionEpochId", epoch), ("SourceNodeKeys", previousKeys)), token).ConfigureAwait(false);
            if (rows.Count <= WorkflowDefinitionCompiler.MaximumNodes * 20)
                previous = rows.GroupBy(row => row.NodeKey, StringComparer.Ordinal).ToDictionary(group => group.Key,
                    group => (IReadOnlyList<WorkflowPreviousActorRecord>)group.ToArray(), StringComparer.Ordinal);
        }
        var selectionPolicies = allSources.Where(source => source.StarterSelection is not null).Select(source => source.StarterSelection!).ToArray();
        Error? selectionError = null;
        if (selectionPolicies.Length > 0 && starterSelections is null)
        {
            var stored = instanceId is { } selectedInstance
                ? await query.QuerySingleOrDefaultAsync<WorkflowStarterSelectionRecord>(WorkflowStarterSelectionSql.Find,
                    WorkflowSqlParameters.Create(("InstanceId", selectedInstance), ("TenantScopeKey", scope.TenantScopeKey)), token).ConfigureAwait(false) : null;
            WorkflowStarterSelectionSnapshot? snapshot = null;
            if (stored is not null && Encoding.UTF8.GetByteCount(stored.SelectionsJson) <= 32768 && stored.SelectionsHash ==
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(stored.SelectionsJson))))
            {
                try { snapshot = JsonSerializer.Deserialize(stored.SelectionsJson, WorkflowJsonSerializerContext.Default.WorkflowStarterSelectionSnapshot); }
                catch (JsonException) { /* 损坏快照进入明确错误及恢复流程，不能回退成当前发起人。 */ }
            }
            if (snapshot?.SchemaVersion == 1)
            {
                var resolved = await starter.ResolveAsync(selectionPolicies, snapshot.Selections, scope, token, false).ConfigureAwait(false);
                starterSelections = resolved.Value; selectionError = resolved.Error;
            }
            else selectionError = new(WorkflowErrorCodes.DefinitionAssigneePolicyInvalid, "The immutable starter selection snapshot is unavailable.", ErrorType.BusinessRule);
        }
        var context = new WorkflowAssigneeRuntimeContext(values ?? new Dictionary<string, JsonElement>(), previous, starterSelections, flowAdministratorUserIds);
        foreach (var node in ccNodes.Values)
        {
            var sources = node.RecipientPolicy!.Sources;
            if (completingNodeKey is not null && sources.Any(source => source.PreviousNodeKey == completingNodeKey)) continue;
            ccSnapshots.Add(node.NodeKey, selectionError is not null && sources.Any(source => source.StarterSelection is not null)
                ? Result<WorkflowCcRecipients>.Failure(selectionError)
                : await coordinator.ResolveCcAsync(node.RecipientPolicy, scope, initiatorId, context, token).ConfigureAwait(false));
        }
        foreach (var current in waits)
        {
            // 当前办理尚未提交，不能预判其真实处理人；缺少准备结果会由执行器保存耐久续办工作。
            if (completingNodeKey is not null && Sources(current).Any(source => source.PreviousNodeKey == completingNodeKey)) continue;
            if (selectionError is not null && Sources(current).Any(source => source.StarterSelection is not null))
            {
                snapshots.Add(current.NextHumanNodeKey!, Result<WorkflowHumanActivityAssignees>.Failure(selectionError)); continue;
            }
            snapshots.Add(current.NextHumanNodeKey!, current.ClaimPolicy is { } claim
                    ? await coordinator.ResolveClaimAsync(current.AssigneePolicy, claim, scope, initiatorId, token, context).ConfigureAwait(false)
                    : current.VotePolicy is { } vote
                    ? await coordinator.ResolveVoteAsync(current.AssigneePolicy, vote, scope, initiatorId, token, context).ConfigureAwait(false)
                    : current.TaskPolicy is { } task
                    ? await coordinator.ResolveTaskAsync(current.AssigneePolicy, task, scope, initiatorId, token, context).ConfigureAwait(false)
                    : await coordinator.ResolveAsync(current.AssigneePolicy, current.ApprovalPolicy, scope, initiatorId, token, context).ConfigureAwait(false));
        }
        return new(snapshots, ccSnapshots);
    }

    private static IEnumerable<WorkflowAssigneeSource> Sources(WorkflowRuntimeTransition transition) =>
        transition.ApprovalPolicy?.GroupConfiguration is { } groups
            ? groups.Groups.SelectMany(group => group.AssigneePolicy.Sources) : transition.AssigneePolicy.Sources;
}

/// <summary>单次推进的准备结果不跨请求或工作项复用，未准备的等待点必须耐久续办。</summary>
internal sealed class WorkflowPreparedHumanActivities(IReadOnlyDictionary<string, Result<WorkflowHumanActivityAssignees>> snapshots,
    IReadOnlyDictionary<string, Result<WorkflowCcRecipients>>? ccSnapshots = null)
{
    public static WorkflowPreparedHumanActivities Empty { get; } = new(new Dictionary<string, Result<WorkflowHumanActivityAssignees>>());
    public bool TryGet(string nodeKey, out Result<WorkflowHumanActivityAssignees> snapshot) =>
        snapshots.TryGetValue(nodeKey, out snapshot!);
    public bool TryGetCc(string nodeKey, out Result<WorkflowCcRecipients> snapshot)
    {
        snapshot = null!;
        return ccSnapshots is not null && ccSnapshots.TryGetValue(nodeKey, out snapshot!);
    }
}

/// <summary>本次抄送激活的固定成员及权威来源；事务内不重新读取身份目录。</summary>
internal sealed record WorkflowCcRecipients(IReadOnlyList<Guid> UserIds, string ResolutionJson, string ResolutionHash);
