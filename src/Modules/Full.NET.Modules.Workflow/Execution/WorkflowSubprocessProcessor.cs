using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>同作用域子结果通过自有耐久事实接纳；创建、取消、文件准备均在父接纳事务外执行。</summary>
internal sealed partial class WorkflowSubprocessProcessor(IQueryExecutor query, ICommandExecutor commands, ICommandTransaction transaction,
    ICurrentTenantContextWriter tenantWriter, IActiveTenantContextResolver tenants, IOptions<DatabaseOptions> database,
    IOptions<WorkflowRecoveryWorkerOptions> options, IClock clock, IIdGenerator ids, WorkflowSubprocessScanCursor cursor,
    WorkflowInstanceManagementService instances, WorkflowAutomaticOutputCoordinator outputs, WorkflowActivityScheduler scheduler,
    IHostUserBatchSelectionDirectory hostUsers, ITenantMemberSelectionDirectory tenantMembers, WorkflowMultiInstanceSourcePreparation sourcePreparation,
    WorkflowSubprocessStarterPreparation starterPreparation)
{
    public async Task<int> ProcessPendingAsync(CancellationToken token)
    {
        var take = Math.Clamp(options.Value.BatchSize, 1, 8); var after = cursor.Read(); IReadOnlyList<WorkflowSubprocessCallRecord> rows;
        tenantWriter.SetHost();
        try { rows = await query.QueryAsync<WorkflowSubprocessCallRecord>(database.Value.Provider == DatabaseProvider.SqlServer ? WorkflowSubprocessSql.PendingSqlServer : WorkflowSubprocessSql.PendingMySql,
            WorkflowSqlParameters.Create(("Take", take), ("HasAfter", after.Id is null ? 0 : 1), ("AfterUpdatedAtUtc", after.UpdatedAtUtc ?? clock.UtcNow), ("AfterId", after.Id)), token).ConfigureAwait(false); }
        finally { tenantWriter.Clear(); }
        cursor.Advance(rows, take); var processed = 0;
        foreach (var candidate in rows)
        {
            var root = await ReadRootAsync(candidate.InstanceId, candidate.TenantScopeKey, token).ConfigureAwait(false);
            if (root is null || root.TenantScopeKey != (root.TenantId is { } id ? $"tenant:{id:N}" : "host")) continue;
            if (root.TenantId is { } tenantId) { var tenant = await tenants.ResolveActiveByIdAsync(tenantId, token).ConfigureAwait(false);
                if (tenant is null) continue; tenantWriter.SetTenant(tenant); } else tenantWriter.SetHost();
            try { if (await ProcessCallAsync(root, candidate, token).ConfigureAwait(false)) processed++; }
            finally { tenantWriter.Clear(); }
        }
        return processed;
    }

    private async Task<bool> ProcessCallAsync(WorkflowInstanceRecord parent, WorkflowSubprocessCallRecord candidate, CancellationToken token)
    {
        var call = await ReadCallAsync(candidate, token).ConfigureAwait(false);
        if (call is null || call.StatusKey is "applied" or "cancelled") return false;
        var context = await LoadAsync(parent, call, token).ConfigureAwait(false);
        if (context is null) return await FaultAsync(call, "workflow.subprocess.binding_invalid", token).ConfigureAwait(false);
        if (await ProcessTimeoutAsync(context, token).ConfigureAwait(false) is { } timed) return timed;
        if (context.Policy.ExecutionSet is not null) return await ProcessMultiAsync(context, token).ConfigureAwait(false);
        var item = context.Item!;
        var stepRevision = await query.QuerySingleOrDefaultAsync<long?>(WorkflowSubprocessSql.ActiveStepRevision, P(call, ("Id", call.Id)), token).ConfigureAwait(false);
        if (parent.StatusKey is "cancelled" or "rejected" || stepRevision is null)
        {
            if (!call.CancellationRequested) return await MarkCancellationAsync(call, token).ConfigureAwait(false);
            if (item.ChildInstanceId is null) return await RecordAsync(context, "cancelled", null, null, true, token).ConfigureAwait(false);
            var child = await ReadChildAsync(context, token).ConfigureAwait(false);
            if (child is null) return await FaultAsync(call, "workflow.subprocess.binding_invalid", token).ConfigureAwait(false);
            if (child.StatusKey is "active" or "suspended")
            {
                var cancelled = await instances.CancelFromParentAsync(child.Id, new(parent.Id, call.Id, item.Id, item.InputHash), token).ConfigureAwait(false);
                return cancelled.IsSuccess;
            }
            // 子根的取消不能伪装成外部效果已对账；嵌套等待也须先完成取消收敛。
            if (await query.QuerySingleOrDefaultAsync<int>(WorkflowSubprocessSql.CountChildUncertainEffects,
                    WorkflowSqlParameters.Create(("InstanceId", child.Id), ("TenantScopeKey", call.TenantScopeKey)), token).ConfigureAwait(false) != 0 ||
                await query.QuerySingleOrDefaultAsync<int>(WorkflowSubprocessSql.CountUnsettled,
                    WorkflowSqlParameters.Create(("InstanceId", child.Id), ("TenantScopeKey", call.TenantScopeKey)), token).ConfigureAwait(false) != 0)
                return await FaultAsync(call, "workflow.subprocess.child_outcome_unknown", token).ConfigureAwait(false);
            return await RecordAsync(context, child.StatusKey == "completed" ? "succeeded" : child.StatusKey == "rejected" ? "rejected" : "cancelled",
                null, child.Revision, true, token).ConfigureAwait(false);
        }
        if (parent.StatusKey == "completed") return await FaultAsync(call, "workflow.subprocess.parent_terminal", token).ConfigureAwait(false);
        if (call.StatusKey == "result_ready") return parent.StatusKey == "active" && await ApplyAsync(context, token).ConfigureAwait(false);
        if (item.ChildInstanceId is null)
        {
            if (parent.StatusKey != "active") return false;
            var active = parent.TenantId is null
                ? (await hostUsers.FindActiveHostUsersAsync([item.StarterUserId], token).ConfigureAwait(false)).ContainsKey(item.StarterUserId)
                : (await tenantMembers.FindActiveTenantMembersAsync([item.StarterUserId], token).ConfigureAwait(false)).ContainsKey(item.StarterUserId);
            if (!active) return await FaultAsync(call, "workflow.subprocess.starter_unavailable", token).ConfigureAwait(false);
            using var input = JsonDocument.Parse(item.InputJson);
            var started = await instances.StartAsync(item.StarterUserId, new(call.ChildDefinitionVersionId, WorkflowChildStartContext.BusinessType,
                item.Id.ToString("D"), input.RootElement.Clone(), "workflow-child-" + item.Id.ToString("N")), token,
                childContext: new(parent.Id, call.Id, item.Id, item.InputHash)).ConfigureAwait(false);
            if (started.IsSuccess) return true;
            // 另一工作者可能已在同一个父锁下关联成功，不能把其成功改写为故障。
            var latest = await ReadItemAsync(call, item.Id, token).ConfigureAwait(false);
            if (latest?.ChildInstanceId is not null) return false;
            var current = await ReadRootAsync(parent.Id, call.TenantScopeKey, token).ConfigureAwait(false);
            return current?.StatusKey == "active" && await FaultAsync(call, "workflow.subprocess.child_unavailable", token).ConfigureAwait(false);
        }
        var completedChild = await ReadChildAsync(context, token).ConfigureAwait(false);
        if (completedChild is null) return await FaultAsync(call, "workflow.subprocess.binding_invalid", token).ConfigureAwait(false);
        if (completedChild.StatusKey is "active" or "suspended") return false;
        if (completedChild.StatusKey != "completed") return await RecordAsync(context,
            completedChild.StatusKey == "rejected" ? "rejected" : "cancelled", null, completedChild.Revision, false, token).ConfigureAwait(false);
        var submission = await query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance,
            WorkflowSqlParameters.Create(("InstanceId", completedChild.Id), ("TenantScopeKey", call.TenantScopeKey)), token).ConfigureAwait(false);
        Dictionary<string, JsonElement>? childValues = null;
        try { if (submission is not null && Encoding.UTF8.GetByteCount(submission.SubmissionJson) <= WorkflowSubprocessPolicy.MaximumInputBytes)
                childValues = JsonSerializer.Deserialize(submission.SubmissionJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement); }
        catch (JsonException) { }
        if (childValues is null || !WorkflowFormValueValidator.Validate(context.ChildSchema, childValues) ||
            !context.Policy.TryCreateOutput(childValues, context.ParentSchema, out var patch)) return await FaultAsync(call, "workflow.subprocess.output_invalid", token).ConfigureAwait(false);
        var json = JsonSerializer.Serialize(patch.ToDictionary(pair => pair.Key, pair => pair.Value), WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
        return await RecordAsync(context, "succeeded", json, completedChild.Revision, false, token).ConfigureAwait(false);
    }

    private async Task<SubprocessContext?> LoadAsync(WorkflowInstanceRecord parent, WorkflowSubprocessCallRecord call, CancellationToken token)
    {
        if (call.Revision < 1 || call.InputFormRevision < 1 || call.Depth is < 1 or > WorkflowSubprocessPolicy.MaximumDepth ||
            Encoding.UTF8.GetByteCount(call.ParentInputJson) > WorkflowSubprocessPolicy.MaximumInputBytes ||
            call.TenantScopeKey != parent.TenantScopeKey || call.InstanceId != parent.Id) return null;
        var items = await query.QueryAsync<WorkflowSubprocessItemRecord>(WorkflowSubprocessSql.Items, P(call, ("CallId", call.Id)), token).ConfigureAwait(false);
        if (items.Count > WorkflowMultiInstancePolicy.MaximumItems || items.Any(item => item.InstanceId != parent.Id || item.CallId != call.Id || item.TenantScopeKey != parent.TenantScopeKey)) return null;
        if (await query.QuerySingleOrDefaultAsync<string>(WorkflowSubprocessSql.StepStatus, P(call, ("Id", call.Id)), token).ConfigureAwait(false) is null) return null;
        var asset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", parent.DefinitionVersionId), ("TenantScopeKey", parent.TenantScopeKey)), token).ConfigureAwait(false);
        var childAsset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", call.ChildDefinitionVersionId), ("TenantScopeKey", parent.TenantScopeKey)), token).ConfigureAwait(false);
        if (asset is null || childAsset is null) return null;
        try
        {
            var draft = JsonSerializer.Deserialize(asset.CanonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
            var schema = JsonSerializer.Deserialize(asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
            var childSchema = JsonSerializer.Deserialize(childAsset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
            var original = JsonSerializer.Deserialize(call.ParentInputJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
            var node = draft?.Nodes.SingleOrDefault(node => node.NodeKey == call.NodeKey);
            if (node?.NodeTypeKey != WorkflowSubprocessPolicy.NodeType || schema is null || childSchema is null || original is null ||
                node.Config.GetRawText() != call.PolicyJson || !WorkflowSubprocessPolicy.TryRead(node.Config, schema, out var policy) ||
                !node.Config.TryGetProperty("boundSubprocess", out var data) || !WorkflowBoundSubprocess.TryRead(data, call.ChildDefinitionVersionId, out var bound) ||
                !WorkflowSubprocessBindings.TryBindAsset(childAsset, policy, schema, out var actual, requireEnabled: false) || actual != bound ||
                bound.FormVersionId != call.ChildFormVersionId || bound.ActivityBudget != call.ChildActivityBudget ||
                bound.DefinitionHash != call.DefinitionHash || bound.FormHash != call.FormHash) return null;
            var set = await ReadSetAsync(call, token).ConfigureAwait(false);
            if (policy.ExecutionSet is null)
            {
                if (set is not null || items.Count != 1 || items[0].SequenceNo != 1 || items[0].ItemKey != "single" || items[0].StarterUserId != parent.StartedById ||
                    WorkflowSubprocessBindings.Hash(items[0].InputJson) != items[0].InputHash ||
                    !policy.TryCreateInput(original, childSchema, out var input) || input != items[0].InputJson) return null;
            }
            else if (set is null || set.StatusKey == "preparing" && items.Count != 0 || set.StatusKey != "preparing" &&
                !(set.StatusKey == "faulted" && set.SourceJson is null && items.Count == 0) &&
                (!WorkflowMultiInstanceSnapshotRules.TryRead(set, policy.ExecutionSet, original, out var snapshot) ||
                 !WorkflowMultiInstanceSnapshotRules.MatchesItems(policy, childSchema, original, snapshot, items, parent.StartedById))) return null;
            return new(parent, call, items.FirstOrDefault(), policy, schema, childSchema, original, items, set);
        }
        catch (JsonException) { return null; }
    }

    private async Task<WorkflowInstanceRecord?> ReadChildAsync(SubprocessContext context, CancellationToken token)
    {
        if (context.Item is not { } item) return null;
        var child = item.ChildInstanceId is { } id ? await ReadRootAsync(id, context.Call.TenantScopeKey, token).ConfigureAwait(false) : null;
        return child is not null && child.DefinitionVersionId == context.Call.ChildDefinitionVersionId && child.FormVersionId == context.Call.ChildFormVersionId &&
            child.BusinessType == WorkflowChildStartContext.BusinessType && child.BusinessId == item.Id.ToString("D") && child.StartedById == item.StarterUserId ? child : null;
    }

    private async Task<bool> RecordAsync(SubprocessContext context, string status, string? json, long? childRevision, bool closing, CancellationToken token)
    {
        var saved = await transaction.ExecuteResultAsync(async inner => {
            var state = await LockAsync(context.Call, inner).ConfigureAwait(false); if (state is null) return Invalid();
            var (root, call) = state.Value; var item = await ReadItemAsync(call, context.Item!.Id, inner).ConfigureAwait(false);
            if (item is null || item.ChildInstanceId != context.Item!.ChildInstanceId || call.StatusKey is "applied" or "cancelled") return Result<bool>.Success(false);
            var failedRoot = root.StatusKey is "cancelled" or "rejected"; var hash = json is null ? null : WorkflowSubprocessBindings.Hash(json);
            if (!failedRoot && await TimeoutOwnsClosingAsync(call, inner).ConfigureAwait(false)) return Result<bool>.Success(false);
            if (closing && !failedRoot && await query.QuerySingleOrDefaultAsync<long?>(WorkflowSubprocessSql.ActiveStepRevision, P(call, ("Id", call.Id)), inner).ConfigureAwait(false) is not null) return Result<bool>.Success(false);
            if (item.StatusKey is "succeeded" or "rejected" or "cancelled") return item.StatusKey == status && item.ResultHash == hash ? Result<bool>.Success(false) : Invalid();
            if (await commands.ExecuteAsync(WorkflowSubprocessSql.RecordItemResult, P(call, ("Id", item.Id), ("CallId", call.Id), ("Revision", item.Revision),
                    ("StatusKey", status), ("ResultJson", json), ("ResultHash", hash), ("ChildRevision", childRevision), ("Now", clock.UtcNow)), inner).ConfigureAwait(false) != 1) return Invalid();
            var next = failedRoot || closing || call.CancellationRequested ? "cancelled" : status == "succeeded" ? "result_ready" : "faulted";
            if (!await SetStatusAsync(call, next, next == "cancelled", next == "faulted" ? "workflow.subprocess.child_rejected" : null, inner).ConfigureAwait(false)) return Invalid();
            if (next == "faulted" && root.StatusKey == "active" && !await SuspendAsync(root, inner).ConfigureAwait(false)) return Invalid();
            return Result<bool>.Success(true);
        }, token).ConfigureAwait(false);
        return saved.IsSuccess && saved.Value;
    }

    private async Task<bool> ApplyAsync(SubprocessContext context, CancellationToken token)
    {
        var call = context.Call; var item = context.Item!;
        if (item.StatusKey != "succeeded" || item.ResultJson is null || item.ResultHash != WorkflowSubprocessBindings.Hash(item.ResultJson))
            return await FaultAsync(call, "workflow.subprocess.output_invalid", token).ConfigureAwait(false);
        Dictionary<string, JsonElement>? patch;
        try { patch = JsonSerializer.Deserialize(item.ResultJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement); }
        catch (JsonException) { patch = null; }
        if (patch is null || !patch.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(context.Policy.OutputFields.Keys)) return await FaultAsync(call, "workflow.subprocess.output_invalid", token).ConfigureAwait(false);
        if (!context.Policy.TryAddStarterOutput(context.ParentSchema, patch, context.Items, out var completedPatch)) return await FaultAsync(call, "workflow.subprocess.output_invalid", token).ConfigureAwait(false);
        return await ApplyPatchAsync(context, completedPatch, null, token).ConfigureAwait(false);
    }

    private async Task<bool> ApplyPatchAsync(SubprocessContext context, IReadOnlyDictionary<string, JsonElement> patch,
        WorkflowMultiInstanceSetRecord? expectedSet, CancellationToken token)
    {
        var call = context.Call;
        var snapshot = await outputs.ReadSnapshotAsync(context.Parent, call.ParallelJoinId, call.ParallelBranchKey, context.ParentSchema, token).ConfigureAwait(false);
        if (snapshot is null || patch.Keys.Any(key => !Same(context.Original, snapshot.Values, key))) return await FaultAsync(call, "workflow.subprocess.output_conflict", token).ConfigureAwait(false);
        var nextValues = snapshot.Values.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
        foreach (var pair in patch) nextValues[pair.Key] = pair.Value.Clone();
        if (!WorkflowFormValueValidator.Validate(context.ParentSchema, nextValues)) return await FaultAsync(call, "workflow.subprocess.output_invalid", token).ConfigureAwait(false);
        var input = new WorkflowAutomaticInput(call.Id, snapshot.Json, snapshot.Hash, snapshot.FormRevision, call.ParallelJoinId, call.ParallelBranchKey);
        var output = new WorkflowFieldMutationOutput(nextValues, patch, patch.Count > 0);
        var selected = expectedSet is null ? context.Items : context.Items.Where(item => item.StatusKey == "succeeded" && item.ResultJson is not null).ToArray();
        if (expectedSet is not null)
        {
            var chosen = await query.QueryAsync<Guid>(WorkflowMultiInstanceSql.Choices, P(call, ("CallId", call.Id)), token).ConfigureAwait(false);
            selected = selected.Where(item => chosen.Contains(item.Id)).ToArray();
        }
        var fileSources = WorkflowSubprocessAttachmentProjection.PrepareOutput(context.Policy, context.ParentSchema, patch, selected);
        if (fileSources is null) return await FaultAsync(call, "workflow.subprocess.output_invalid", token).ConfigureAwait(false);
        var prepared = await outputs.PrepareAsync(context.Parent, input, context.ParentSchema, snapshot.Values, output,
            key => context.Policy.FieldPolicies.TryGetValue(key, out var grant) && grant != "hidden", token, fileSources).ConfigureAwait(false);
        if (prepared is null) return await FaultAsync(call, "workflow.subprocess.output_invalid", token).ConfigureAwait(false);
        try
        {
            var applied = await transaction.ExecuteResultAsync(async inner => {
                var state = await LockAsync(call, inner).ConfigureAwait(false); if (state is null) return Invalid();
                var (root, current) = state.Value;
                if (root.StatusKey != "active" || current.StatusKey != "result_ready" || current.CancellationRequested || current.Revision != call.Revision) return Result<bool>.Success(false);
                if (context.Policy.Timeout is not null)
                {
                    var deadline = await ReadTimeoutAsync(current, inner).ConfigureAwait(false);
                    if (deadline?.StatusKey != "waiting" || !await WorkflowSubprocessTimeoutLifecycle.UpdateAsync(deadline with { StatusKey = "applied" }, commands, inner).ConfigureAwait(false)) return Invalid();
                }
                if (expectedSet is not null)
                {
                    var currentSet = await ReadSetAsync(current, inner).ConfigureAwait(false);
                    if (currentSet != expectedSet || currentSet.StatusKey != "result_ready" ||
                        !await UpdateSetAsync(currentSet with { StatusKey = "applied" }, inner).ConfigureAwait(false)) return Invalid();
                }
                if (!await outputs.CommitAsync(root, input, context.ParentSchema, snapshot.Values, prepared, inner).ConfigureAwait(false)) return Invalid();
                var revision = await query.QuerySingleOrDefaultAsync<long?>(WorkflowSubprocessSql.ActiveStepRevision, P(current, ("Id", current.Id)), inner).ConfigureAwait(false);
                if (revision is null || await commands.ExecuteAsync(WorkflowSql.CompleteStepWithRevision, WorkflowSqlParameters.Create(("Id", current.Id), ("InstanceId", root.Id),
                        ("Revision", revision.Value), ("StatusKey", "completed"), ("CompletedAtUtc", clock.UtcNow)), inner).ConfigureAwait(false) != 1 ||
                    !await SetStatusAsync(current, "applied", false, null, inner).ConfigureAwait(false)) return Invalid();
                var sequence = await query.QuerySingleOrDefaultAsync<long>(WorkflowSql.FindNextStepExecutionSequence, WorkflowSqlParameters.Create(("InstanceId", root.Id)), inner).ConfigureAwait(false);
                await scheduler.EnqueueAsync(root, await WorkflowExecutionEpochScope.ReadForStepAsync(query, root.Id, root.TenantScopeKey, current.Id, inner).ConfigureAwait(false) ?? Guid.Empty, current.ContinuationNodeKey, nextValues, snapshot.FormRevision + (output.Matched ? 1 : 0), sequence,
                    current.ParallelJoinId, current.ParallelBranchKey, clock.UtcNow, inner, sourceStepId: current.Id).ConfigureAwait(false);
                if (await commands.ExecuteAsync(WorkflowSql.AdvanceInstanceWithRevision, WorkflowSqlParameters.Create(("Id", root.Id), ("TenantScopeKey", current.TenantScopeKey), ("Revision", root.Revision)), inner).ConfigureAwait(false) != 1) return Invalid();
                await commands.ExecuteAsync(WorkflowSql.InsertDomainAudit, WorkflowSqlParameters.Create(("Id", ids.NewId()), ("TenantId", root.TenantId), ("ScopeKey", root.ScopeKey),
                    ("InstanceId", root.Id), ("OperationKey", "activity.execute"), ("ActorUserId", null), ("ResourceTypeKey", "step"), ("ResourceId", current.Id),
                    ("OutcomeKey", "succeeded"), ("DetailJson", "{\"nodeTypeKey\":\"process.subprocess\",\"phaseKey\":\"applied\"}"),
                    ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()), ("CreatedAtUtc", clock.UtcNow)), inner).ConfigureAwait(false);
                return Result<bool>.Success(true);
            }, token).ConfigureAwait(false);
            return applied.IsSuccess && applied.Value;
        }
        finally { await outputs.FinishAsync(prepared, CancellationToken.None).ConfigureAwait(false); }
    }

    private async Task<bool> MarkCancellationAsync(WorkflowSubprocessCallRecord candidate, CancellationToken token)
    {
        var result = await transaction.ExecuteResultAsync(async inner => {
            var state = await LockAsync(candidate, inner).ConfigureAwait(false); if (state is null) return Invalid();
            var (root, call) = state.Value;
            if (call.CancellationRequested || call.StatusKey is "applied" or "cancelled") return Result<bool>.Success(false);
            if (root.StatusKey is not ("cancelled" or "rejected") && await query.QuerySingleOrDefaultAsync<long?>(WorkflowSubprocessSql.ActiveStepRevision,
                    P(call, ("Id", call.Id)), inner).ConfigureAwait(false) is not null) return Result<bool>.Success(false);
            return await SetStatusAsync(call, call.StatusKey, true, call.ErrorCode, inner).ConfigureAwait(false) ? Result<bool>.Success(true) : Invalid();
        }, token).ConfigureAwait(false);
        return result.IsSuccess && result.Value;
    }

    private async Task<bool> FaultAsync(WorkflowSubprocessCallRecord candidate, string code, CancellationToken token)
    {
        var result = await transaction.ExecuteResultAsync(async inner => {
            var state = await LockAsync(candidate, inner).ConfigureAwait(false); if (state is null) return Invalid();
            var (root, call) = state.Value;
            if (call.StatusKey is "applied" or "cancelled") return Result<bool>.Success(false);
            if (await TimeoutOwnsClosingAsync(call, inner).ConfigureAwait(false)) return Result<bool>.Success(false);
            if (call.StatusKey == "faulted" && call.ErrorCode == code && root.StatusKey != "active") return Result<bool>.Success(false);
            if (!await SetStatusAsync(call, "faulted", call.CancellationRequested, code, inner).ConfigureAwait(false) ||
                root.StatusKey == "active" && !await SuspendAsync(root, inner).ConfigureAwait(false)) return Invalid();
            var detail = WorkflowJsonCanonicalizer.Compile(writer => { writer.WriteStartObject(); writer.WriteString("nodeTypeKey", WorkflowSubprocessPolicy.NodeType);
                writer.WriteString("phaseKey", "faulted"); writer.WriteString("errorCode", code); writer.WriteEndObject(); }).CanonicalJson;
            await commands.ExecuteAsync(WorkflowSql.InsertDomainAudit, WorkflowSqlParameters.Create(("Id", ids.NewId()), ("TenantId", root.TenantId), ("ScopeKey", root.ScopeKey),
                ("InstanceId", root.Id), ("OperationKey", "activity.execute"), ("ActorUserId", null), ("ResourceTypeKey", "step"), ("ResourceId", call.Id),
                ("OutcomeKey", "failed"), ("DetailJson", detail), ("CreatedAtUtc", clock.UtcNow),
                ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson())), inner).ConfigureAwait(false);
            return Result<bool>.Success(true);
        }, token).ConfigureAwait(false);
        return result.IsSuccess && result.Value;
    }
    private async Task<(WorkflowInstanceRecord Root, WorkflowSubprocessCallRecord Call)?> LockAsync(WorkflowSubprocessCallRecord call, CancellationToken token)
    {
        if (await commands.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction, P(call, ("Id", call.InstanceId)), token).ConfigureAwait(false) != 1) return null;
        var root = await ReadRootAsync(call.InstanceId, call.TenantScopeKey, token).ConfigureAwait(false); var current = await ReadCallAsync(call, token).ConfigureAwait(false);
        return root is null || current is null ? null : (root, current);
    }
    private async Task<bool> SetStatusAsync(WorkflowSubprocessCallRecord call, string status, bool cancel, string? code, CancellationToken token) =>
        await commands.ExecuteAsync(WorkflowSubprocessSql.SetStatus, P(call, ("Id", call.Id), ("Revision", call.Revision), ("StatusKey", status),
            ("CancellationRequested", cancel), ("ErrorCode", code), ("Now", clock.UtcNow)), token).ConfigureAwait(false) == 1;
    private async Task<bool> SuspendAsync(WorkflowInstanceRecord root, CancellationToken token) => await commands.ExecuteAsync(WorkflowSql.SuspendInstanceWithRevision,
        WorkflowSqlParameters.Create(("Id", root.Id), ("TenantScopeKey", root.TenantScopeKey), ("Revision", root.Revision)), token).ConfigureAwait(false) == 1;
    private Task<WorkflowInstanceRecord?> ReadRootAsync(Guid id, string scope, CancellationToken token) => query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
        WorkflowSqlParameters.Create(("Id", id), ("TenantScopeKey", scope)), token);
    private Task<WorkflowSubprocessCallRecord?> ReadCallAsync(WorkflowSubprocessCallRecord call, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<WorkflowSubprocessCallRecord>(WorkflowSubprocessSql.Find, P(call, ("Id", call.Id)), token);
    private Task<WorkflowSubprocessItemRecord?> ReadItemAsync(WorkflowSubprocessCallRecord call, Guid id, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<WorkflowSubprocessItemRecord>(WorkflowSubprocessSql.FindItem, P(call, ("Id", id), ("CallId", call.Id)), token);
    private static object P(WorkflowSubprocessCallRecord call, params (string Name, object? Value)[] values) => WorkflowSqlParameters.Create(
        new[] { ("InstanceId", (object?)call.InstanceId), ("TenantScopeKey", (object?)call.TenantScopeKey) }.Concat(values).ToArray());
    private static bool Same(IReadOnlyDictionary<string, JsonElement> original, IReadOnlyDictionary<string, JsonElement> current, string key) =>
        original.TryGetValue(key, out var before) == current.TryGetValue(key, out var after) && (before.ValueKind == JsonValueKind.Undefined || JsonElement.DeepEquals(before, after));
    private static Result<bool> Invalid() => Result<bool>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
    private sealed record SubprocessContext(WorkflowInstanceRecord Parent, WorkflowSubprocessCallRecord Call, WorkflowSubprocessItemRecord? Item,
        WorkflowSubprocessPolicy Policy, WorkflowFormSchema ParentSchema, WorkflowFormSchema ChildSchema, IReadOnlyDictionary<string, JsonElement> Original, IReadOnlyList<WorkflowSubprocessItemRecord> Items, WorkflowMultiInstanceSetRecord? Set);
}
