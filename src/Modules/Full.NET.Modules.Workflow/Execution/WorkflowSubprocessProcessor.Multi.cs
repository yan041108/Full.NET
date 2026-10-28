using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Execution;

internal sealed partial class WorkflowSubprocessProcessor
{
    private async Task<bool> ProcessMultiAsync(SubprocessContext context, CancellationToken token)
    {
        var call = context.Call; var parent = context.Parent; var policy = context.Policy.ExecutionSet!; var set = context.Set!;
        var step = await query.QuerySingleOrDefaultAsync<long?>(WorkflowSubprocessSql.ActiveStepRevision, P(call, ("Id", call.Id)), token).ConfigureAwait(false);
        var cancelled = parent.StatusKey is "cancelled" or "rejected" || step is null || call.CancellationRequested;
        if (cancelled && !call.CancellationRequested) return await MarkCancellationAsync(call, token).ConfigureAwait(false);
        if (!cancelled && parent.StatusKey == "completed") return await MultiFaultAsync(context, "workflow.subprocess.parent_terminal", token).ConfigureAwait(false);
        if (set.StatusKey == "preparing")
        {
            if (cancelled) return await FinishMultiCancellationAsync(context, token).ConfigureAwait(false);
            if (parent.StatusKey != "active") return false;
            var source = await sourcePreparation.PrepareAsync(policy, context.Original, token).ConfigureAwait(false);
            if (source is null) return await MultiFaultAsync(context, "workflow.subprocess.source_invalid", token).ConfigureAwait(false);
            return await FreezeMultiAsync(context, source, token).ConfigureAwait(false);
        }
        if (!cancelled && set.StatusKey == "result_ready")
        {
            if (parent.StatusKey != "active") return false;
            Dictionary<string, JsonElement>? patch = null;
            try { if (set.OutputJson is not null && set.OutputHash == WorkflowSubprocessBindings.Hash(set.OutputJson))
                patch = JsonSerializer.Deserialize(set.OutputJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement); }
            catch (JsonException) { }
            var expectedFields = set.ItemCount == 0 ? context.Policy.Starter?.ResultFieldKey is { } result ? new[] { result } : Array.Empty<string>() : context.Policy.WrittenFields;
            if (patch is null || !patch.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expectedFields)) return await MultiFaultAsync(context, "workflow.subprocess.output_invalid", token).ConfigureAwait(false);
            return await ApplyPatchAsync(context, patch, set, token).ConfigureAwait(false);
        }
        if (cancelled || set.StatusKey is "closing" or "faulted") return await CloseMultiAsync(context, cancelled, token).ConfigureAwait(false);
        if (set.StatusKey != "running") return false;
        var decision = WorkflowMultiInstanceDecision.Evaluate(policy, Facts(context.Items));
        if (decision.StateKey == "select") return await SealMultiAsync(context, decision.SelectedIds, token).ConfigureAwait(false);
        if (decision.StateKey != "wait") return await MultiFaultAsync(context, "workflow.subprocess.threshold_unreachable", token).ConfigureAwait(false);
        // 先接纳已结束的真实子根，再考虑创建新项；不能因为扫描顺序把已知成功门槛遗漏掉。
        foreach (var item in context.Items.Where(item => item.StatusKey == "running"))
        {
            var child = await ReadChildAsync(context with { Item = item }, token).ConfigureAwait(false);
            if (child is null) return await MultiFaultAsync(context, "workflow.subprocess.binding_invalid", token).ConfigureAwait(false);
            if (child.StatusKey is not ("active" or "suspended")) return await CaptureMultiChildAsync(context, item, child, token).ConfigureAwait(false);
        }
        if (parent.StatusKey != "active" || policy.ModeKey == "serial" && context.Items.Any(item => item.StatusKey == "running")) return false;
        var pending = context.Items.FirstOrDefault(item => item.StatusKey == "pending");
        if (pending is null) return false;
        var active = parent.TenantId is null
            ? (await hostUsers.FindActiveHostUsersAsync([pending.StarterUserId], token).ConfigureAwait(false)).ContainsKey(pending.StarterUserId)
            : (await tenantMembers.FindActiveTenantMembersAsync([pending.StarterUserId], token).ConfigureAwait(false)).ContainsKey(pending.StarterUserId);
        if (!active) return await MultiFaultAsync(context, "workflow.subprocess.starter_unavailable", token).ConfigureAwait(false);
        using var input = JsonDocument.Parse(pending.InputJson);
        var started = await instances.StartAsync(pending.StarterUserId, new(call.ChildDefinitionVersionId, WorkflowChildStartContext.BusinessType,
            pending.Id.ToString("D"), input.RootElement.Clone(), "workflow-child-" + pending.Id.ToString("N")), token,
            childContext: new(parent.Id, call.Id, pending.Id, pending.InputHash)).ConfigureAwait(false);
        if (started.IsSuccess) return true;
        // 并发创建或达标封口先提交时，旧扫描无权再把整个集合标为失败。
        var latest = await ReadItemAsync(call, pending.Id, token).ConfigureAwait(false);
        var latestSet = await ReadSetAsync(call, token).ConfigureAwait(false);
        var root = await ReadRootAsync(parent.Id, call.TenantScopeKey, token).ConfigureAwait(false);
        if (latest?.ChildInstanceId is not null || latestSet?.StatusKey != "running" || root?.StatusKey != "active") return false;
        return await MultiFaultAsync(context, "workflow.subprocess.child_unavailable", token).ConfigureAwait(false);
    }

    private async Task<bool> FreezeMultiAsync(SubprocessContext context, WorkflowMultiInstancePreparedSource source, CancellationToken token)
    {
        WorkflowSubprocessPreparedStarters? starters = null;
        if (context.Policy.Starter is { SourceKey: not "parent" } starter)
        {
            starters = await starterPreparation.PrepareAsync(starter, context.Parent.StartedById, context.Original, source,
                new(context.Parent.TenantId, context.Parent.ScopeKey, context.Parent.TenantScopeKey), token).ConfigureAwait(false);
            if (starters is null) return await MultiFaultAsync(context, "workflow.subprocess.starter_unavailable", token).ConfigureAwait(false);
        }
        var inputs = new List<(WorkflowMultiInstanceSeed Seed, string Json)>();
        foreach (var seed in source.Items)
        {
            if (!context.Policy.TryCreateInput(context.Original, context.ChildSchema, out var json, seed))
                return await MultiFaultAsync(context, "workflow.subprocess.input_invalid", token).ConfigureAwait(false);
            inputs.Add((seed, json!));
        }
        var jsonSource = JsonSerializer.Serialize(new WorkflowMultiInstanceSnapshot(source, clock.UtcNow, starters), WorkflowJsonSerializerContext.Default.WorkflowMultiInstanceSnapshot);
        if (Encoding.UTF8.GetByteCount(jsonSource) > WorkflowSubprocessPolicy.MaximumInputBytes) return await MultiFaultAsync(context, "workflow.subprocess.source_invalid", token).ConfigureAwait(false);
        var result = await transaction.ExecuteResultAsync(async inner => {
            var state = await LockAsync(context.Call, inner).ConfigureAwait(false); if (state is null) return Invalid();
            var (root, call) = state.Value; var set = await ReadSetAsync(call, inner).ConfigureAwait(false);
            if (root.StatusKey != "active" || call.CancellationRequested || set?.StatusKey != "preparing" || set.Revision != context.Set!.Revision) return Result<bool>.Success(false);
            if (await query.QuerySingleOrDefaultAsync<long?>(WorkflowSubprocessSql.ActiveStepRevision, P(call, ("Id", call.Id)), inner).ConfigureAwait(false) is null) return Result<bool>.Success(false);
            foreach (var (seed, input) in inputs)
                if (await commands.ExecuteAsync(WorkflowSubprocessSql.InsertItem, P(call, ("Id", ids.NewId()), ("CallId", call.Id), ("ItemKey", seed.ItemKey),
                    ("SequenceNo", seed.SequenceNo), ("StarterUserId", starters?.ByItemKey[seed.ItemKey] ?? root.StartedById), ("InputJson", input), ("InputHash", WorkflowSubprocessBindings.Hash(input)),
                    ("Now", clock.UtcNow)), inner).ConfigureAwait(false) != 1) return Invalid();
            var next = set with { ItemCount = inputs.Count, RequiredSuccesses = context.Policy.ExecutionSet!.RequiredSuccesses(inputs.Count),
                SourceJson = jsonSource, SourceHash = WorkflowSubprocessBindings.Hash(jsonSource), StatusKey = "running" };
            return await UpdateSetAsync(next, inner).ConfigureAwait(false) ? Result<bool>.Success(true) : Invalid();
        }, token).ConfigureAwait(false);
        return result.IsSuccess && result.Value;
    }

    private async Task<bool> CaptureMultiChildAsync(SubprocessContext context, WorkflowSubprocessItemRecord item, WorkflowInstanceRecord child, CancellationToken token)
    {
        string? json = null;
        if (child.StatusKey == "completed")
        {
            var submission = await query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance,
                WorkflowSqlParameters.Create(("InstanceId", child.Id), ("TenantScopeKey", context.Call.TenantScopeKey)), token).ConfigureAwait(false);
            Dictionary<string, JsonElement>? values = null;
            try { if (submission is not null && Encoding.UTF8.GetByteCount(submission.SubmissionJson) <= WorkflowSubprocessPolicy.MaximumInputBytes)
                values = JsonSerializer.Deserialize(submission.SubmissionJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement); }
            catch (JsonException) { }
            if (values is null || !WorkflowFormValueValidator.Validate(context.ChildSchema, values) || !context.Policy.TryCreateOutput(values, context.ParentSchema, out var output))
                return await MultiFaultAsync(context, "workflow.subprocess.output_invalid", token).ConfigureAwait(false);
            json = JsonSerializer.Serialize(output.ToDictionary(pair => pair.Key, pair => pair.Value), WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        }
        return await RecordMultiAsync(context, item, child.StatusKey == "completed" ? "succeeded" : child.StatusKey == "rejected" ? "rejected" : "cancelled", json, child.Revision, token).ConfigureAwait(false);
    }

    private async Task<bool> RecordMultiAsync(SubprocessContext context, WorkflowSubprocessItemRecord item, string status, string? json, long? childRevision, CancellationToken token)
    {
        var result = await transaction.ExecuteResultAsync(async inner => {
            var state = await LockAsync(context.Call, inner).ConfigureAwait(false); if (state is null) return Invalid();
            var (root, call) = state.Value; var current = await ReadItemAsync(call, item.Id, inner).ConfigureAwait(false);
            if (current is null || current.ChildInstanceId != item.ChildInstanceId || current.Revision != item.Revision ||
                current.StatusKey is not ("pending" or "running") || call.StatusKey is "applied" or "cancelled") return Result<bool>.Success(false);
            if (await commands.ExecuteAsync(WorkflowSubprocessSql.RecordItemResult, P(call, ("Id", item.Id), ("CallId", call.Id), ("Revision", current.Revision),
                ("StatusKey", status), ("ResultJson", json), ("ResultHash", json is null ? null : WorkflowSubprocessBindings.Hash(json)), ("ChildRevision", childRevision),
                ("Now", clock.UtcNow)), inner).ConfigureAwait(false) != 1) return Invalid();
            var set = await ReadSetAsync(call, inner).ConfigureAwait(false);
            if (set?.StatusKey == "running" && !call.CancellationRequested && root.StatusKey is "active" or "suspended")
            {
                var items = await query.QueryAsync<WorkflowSubprocessItemRecord>(WorkflowSubprocessSql.Items, P(call, ("CallId", call.Id)), inner).ConfigureAwait(false);
                var selection = WorkflowMultiInstanceDecision.Evaluate(context.Policy.ExecutionSet!, Facts(items));
                // 与首个达标结果同事务封口；并发迟到成功只能补自己的项，不能追加原选择。
                if (selection.StateKey == "select" && !await SealWithinAsync(call, set, items, selection.SelectedIds, inner).ConfigureAwait(false)) return Invalid();
            }
            return Result<bool>.Success(true);
        }, token).ConfigureAwait(false);
        return result.IsSuccess && result.Value;
    }

    private async Task<bool> SealMultiAsync(SubprocessContext context, IReadOnlyList<Guid> observed, CancellationToken token)
    {
        var result = await transaction.ExecuteResultAsync(async inner => {
            var state = await LockAsync(context.Call, inner).ConfigureAwait(false); if (state is null) return Invalid();
            var (root, call) = state.Value; var set = await ReadSetAsync(call, inner).ConfigureAwait(false);
            if (root.StatusKey is not ("active" or "suspended") || call.CancellationRequested || set?.StatusKey != "running") return Result<bool>.Success(false);
            var items = await query.QueryAsync<WorkflowSubprocessItemRecord>(WorkflowSubprocessSql.Items, P(call, ("CallId", call.Id)), inner).ConfigureAwait(false);
            var selection = WorkflowMultiInstanceDecision.Evaluate(context.Policy.ExecutionSet!, Facts(items));
            if (selection.StateKey != "select" || !selection.SelectedIds.SequenceEqual(observed)) return Result<bool>.Success(false);
            return await SealWithinAsync(call, set, items, selection.SelectedIds, inner).ConfigureAwait(false)
                ? Result<bool>.Success(true) : Invalid();
        }, token).ConfigureAwait(false);
        return result.IsSuccess && result.Value;
    }

    private async Task<bool> CloseMultiAsync(SubprocessContext context, bool cancelled, CancellationToken token)
    {
        var call = context.Call; var set = context.Set!;
        var selected = await query.QueryAsync<Guid>(WorkflowMultiInstanceSql.Choices, P(call, ("CallId", call.Id)), token).ConfigureAwait(false);
        var unknown = false;
        foreach (var item in context.Items)
        {
            if (!cancelled && set.StatusKey == "closing" && selected.Contains(item.Id)) continue;
            if (item.ChildInstanceId is null)
            {
                if (item.StatusKey == "pending") return await RecordMultiAsync(context, item, "cancelled", null, null, token).ConfigureAwait(false);
                continue;
            }
            var child = await ReadChildAsync(context with { Item = item }, token).ConfigureAwait(false);
            if (child is null) return await MultiFaultAsync(context, "workflow.subprocess.binding_invalid", token).ConfigureAwait(false);
            if (child.StatusKey is "active" or "suspended")
                return (await instances.CancelFromParentAsync(child.Id, new(context.Parent.Id, call.Id, item.Id, item.InputHash), token).ConfigureAwait(false)).IsSuccess;
            if (!await ChildSettledAsync(child, call, token).ConfigureAwait(false)) { unknown = true; continue; }
            if (item.StatusKey is "pending" or "running") return await RecordMultiAsync(context, item,
                child.StatusKey == "completed" ? "succeeded" : child.StatusKey == "rejected" ? "rejected" : "cancelled", null, child.Revision, token).ConfigureAwait(false);
        }
        if (unknown) return await MultiFaultAsync(context, "workflow.subprocess.child_outcome_unknown", token).ConfigureAwait(false);
        if (cancelled) return await FinishMultiCancellationAsync(context, token).ConfigureAwait(false);
        if (set.StatusKey == "faulted") return await FinishMultiFaultAsync(context, token).ConfigureAwait(false);
        if (!WorkflowMultiInstanceDecision.TryReadSealedSelection(context.Policy.ExecutionSet!, selected, Facts(context.Items), out _) ||
            set.SelectionHash != WorkflowMultiInstanceSnapshotRules.SelectionHash(context.Items.Where(item => selected.Contains(item.Id))))
            return await MultiFaultAsync(context, "workflow.subprocess.selection_invalid", token).ConfigureAwait(false);
        var outputs = new List<WorkflowMultiInstanceOutput>();
        foreach (var item in context.Items.Where(item => selected.Contains(item.Id)))
        {
            Dictionary<string, JsonElement>? patch = null;
            try { if (item.ResultJson is not null && item.ResultHash == WorkflowSubprocessBindings.Hash(item.ResultJson))
                patch = JsonSerializer.Deserialize(item.ResultJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement); }
            catch (JsonException) { }
            var child = await ReadChildAsync(context with { Item = item }, token).ConfigureAwait(false);
            if (patch is null || child?.StatusKey != "completed" || !await ChildSettledAsync(child, call, token).ConfigureAwait(false))
                return await MultiFaultAsync(context, "workflow.subprocess.output_invalid", token).ConfigureAwait(false);
            outputs.Add(new(item.Id, item.ItemKey, patch));
        }
        IReadOnlyDictionary<string, JsonElement> merged = new Dictionary<string, JsonElement>();
        if (context.Items.Count > 0 && !WorkflowMultiInstanceOutputReducer.TryMerge(context.ParentSchema, context.Policy.OutputFields.Keys.ToArray(), context.Policy.ExecutionSet!.OutputReducers, outputs, out merged))
            return await MultiFaultAsync(context, "workflow.subprocess.output_invalid", token).ConfigureAwait(false);
        if (!context.Policy.TryAddStarterOutput(context.ParentSchema, merged, context.Items.Where(item => selected.Contains(item.Id)).ToArray(), out merged))
            return await MultiFaultAsync(context, "workflow.subprocess.output_invalid", token).ConfigureAwait(false);
        var json = JsonSerializer.Serialize(merged.ToDictionary(pair => pair.Key, pair => pair.Value), WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        var result = await transaction.ExecuteResultAsync(async inner => {
            var state = await LockAsync(call, inner).ConfigureAwait(false); if (state is null) return Invalid();
            var (root, current) = state.Value; var currentSet = await ReadSetAsync(current, inner).ConfigureAwait(false);
            if (root.StatusKey is not ("active" or "suspended") || current.CancellationRequested || currentSet != set) return Result<bool>.Success(false);
            if (!await UpdateSetAsync(set with { StatusKey = "result_ready", OutputJson = json, OutputHash = WorkflowSubprocessBindings.Hash(json) }, inner).ConfigureAwait(false) ||
                !await SetStatusAsync(current, "result_ready", false, null, inner).ConfigureAwait(false)) return Invalid();
            return Result<bool>.Success(true);
        }, token).ConfigureAwait(false);
        return result.IsSuccess && result.Value;
    }

    private async Task<bool> MultiFaultAsync(SubprocessContext context, string code, CancellationToken token)
    {
        var result = await transaction.ExecuteResultAsync(async inner => {
            var state = await LockAsync(context.Call, inner).ConfigureAwait(false); if (state is null) return Invalid();
            var (root, call) = state.Value; var set = await ReadSetAsync(call, inner).ConfigureAwait(false);
            if (set is null || call.StatusKey is "applied" or "cancelled") return Result<bool>.Success(false);
            if (await TimeoutOwnsClosingAsync(call, inner).ConfigureAwait(false)) return Result<bool>.Success(false);
            if (set.StatusKey == "faulted" && call.ErrorCode == code) return Result<bool>.Success(false);
            if (!await UpdateSetAsync(set with { StatusKey = "faulted" }, inner).ConfigureAwait(false) ||
                !await SetStatusAsync(call, "running", call.CancellationRequested, code, inner).ConfigureAwait(false) ||
                root.StatusKey == "active" && !await SuspendAsync(root, inner).ConfigureAwait(false)) return Invalid();
            var detail = WorkflowJsonCanonicalizer.Compile(writer => { writer.WriteStartObject(); writer.WriteString("nodeTypeKey", WorkflowSubprocessPolicy.NodeType);
                writer.WriteString("phaseKey", "multi_faulted"); writer.WriteString("errorCode", code); writer.WriteEndObject(); }).CanonicalJson;
            await commands.ExecuteAsync(WorkflowSql.InsertDomainAudit, WorkflowSqlParameters.Create(("Id", ids.NewId()), ("TenantId", root.TenantId),
                ("ScopeKey", root.ScopeKey), ("InstanceId", root.Id), ("OperationKey", "activity.execute"), ("ActorUserId", null), ("ResourceTypeKey", "step"),
                ("ResourceId", call.Id), ("OutcomeKey", "failed"), ("DetailJson", detail), ("CreatedAtUtc", clock.UtcNow),
                ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson())), inner).ConfigureAwait(false);
            return Result<bool>.Success(true);
        }, token).ConfigureAwait(false);
        return result.IsSuccess && result.Value;
    }

    private Task<bool> FinishMultiCancellationAsync(SubprocessContext context, CancellationToken token) => FinishMultiAsync(context, true, token);
    private Task<bool> FinishMultiFaultAsync(SubprocessContext context, CancellationToken token) => FinishMultiAsync(context, false, token);
    private async Task<bool> FinishMultiAsync(SubprocessContext context, bool cancel, CancellationToken token)
    {
        var result = await transaction.ExecuteResultAsync(async inner => {
            var state = await LockAsync(context.Call, inner).ConfigureAwait(false); if (state is null) return Invalid();
            var (root, call) = state.Value; var set = await ReadSetAsync(call, inner).ConfigureAwait(false);
            if (set is null || call.StatusKey is "applied" or "cancelled") return Result<bool>.Success(false);
            if (root.StatusKey is not ("cancelled" or "rejected") && await TimeoutOwnsClosingAsync(call, inner).ConfigureAwait(false)) return Result<bool>.Success(false);
            if (cancel && root.StatusKey is not ("cancelled" or "rejected") && !call.CancellationRequested) return Result<bool>.Success(false);
            var items = await query.QueryAsync<WorkflowSubprocessItemRecord>(WorkflowSubprocessSql.Items, P(call, ("CallId", call.Id)), inner).ConfigureAwait(false);
            if (items.Any(item => item.StatusKey is "pending" or "running")) return Result<bool>.Success(false);
            if (!await UpdateSetAsync(set with { StatusKey = cancel ? "cancelled" : "faulted" }, inner).ConfigureAwait(false) ||
                !await SetStatusAsync(call, cancel ? "cancelled" : "faulted", cancel, cancel ? null : call.ErrorCode, inner).ConfigureAwait(false)) return Invalid();
            return Result<bool>.Success(true);
        }, token).ConfigureAwait(false);
        return result.IsSuccess && result.Value;
    }

    private async Task<bool> ChildSettledAsync(WorkflowInstanceRecord child, WorkflowSubprocessCallRecord call, CancellationToken token) =>
        await query.QuerySingleOrDefaultAsync<int>(WorkflowSubprocessSql.CountChildUncertainEffects, WorkflowSqlParameters.Create(("InstanceId", child.Id), ("TenantScopeKey", call.TenantScopeKey)), token).ConfigureAwait(false) == 0 &&
        await query.QuerySingleOrDefaultAsync<int>(WorkflowSubprocessSql.CountUnsettled, WorkflowSqlParameters.Create(("InstanceId", child.Id), ("TenantScopeKey", call.TenantScopeKey)), token).ConfigureAwait(false) == 0;
    private async Task<bool> SealWithinAsync(WorkflowSubprocessCallRecord call, WorkflowMultiInstanceSetRecord set,
        IReadOnlyList<WorkflowSubprocessItemRecord> items, IReadOnlyList<Guid> selected, CancellationToken token)
    {
        foreach (var id in selected)
            if (await commands.ExecuteAsync(WorkflowMultiInstanceSql.InsertChoice, P(call, ("CallId", call.Id), ("ItemId", id), ("Revision", set.Revision), ("Now", clock.UtcNow)), token).ConfigureAwait(false) != 1) return false;
        return await UpdateSetAsync(set with { StatusKey = "closing", SelectionHash = WorkflowMultiInstanceSnapshotRules.SelectionHash(items.Where(item => selected.Contains(item.Id))) }, token).ConfigureAwait(false);
    }
    private Task<WorkflowMultiInstanceSetRecord?> ReadSetAsync(WorkflowSubprocessCallRecord call, CancellationToken token) => query.QuerySingleOrDefaultAsync<WorkflowMultiInstanceSetRecord>(WorkflowMultiInstanceSql.Find, P(call, ("CallId", call.Id)), token);
    private async Task<bool> UpdateSetAsync(WorkflowMultiInstanceSetRecord set, CancellationToken token) => await commands.ExecuteAsync(WorkflowMultiInstanceSql.Update,
        WorkflowSqlParameters.Create(("CallId", set.CallId), ("InstanceId", set.InstanceId), ("TenantScopeKey", set.TenantScopeKey), ("Revision", set.Revision),
            ("ItemCount", set.ItemCount), ("RequiredSuccesses", set.RequiredSuccesses), ("SourceJson", set.SourceJson), ("SourceHash", set.SourceHash),
            ("SelectionHash", set.SelectionHash), ("OutputJson", set.OutputJson), ("OutputHash", set.OutputHash), ("StatusKey", set.StatusKey), ("Now", clock.UtcNow)), token).ConfigureAwait(false) == 1;
    private static WorkflowMultiInstanceFact[] Facts(IReadOnlyList<WorkflowSubprocessItemRecord> items) => items.Select(item => new WorkflowMultiInstanceFact(item.Id, item.ItemKey, item.SequenceNo, item.StatusKey)).ToArray();
}
