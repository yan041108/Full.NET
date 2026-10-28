using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Execution;

internal sealed partial class WorkflowSubprocessProcessor
{
    private async Task<bool> TimeoutOwnsClosingAsync(WorkflowSubprocessCallRecord call, CancellationToken token) =>
        call.CancellationRequested && (await ReadTimeoutAsync(call, token).ConfigureAwait(false))?.StatusKey is "closing" or "ready";
    private Task<WorkflowSubprocessTimeoutRecord?> ReadTimeoutAsync(WorkflowSubprocessCallRecord call, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<WorkflowSubprocessTimeoutRecord>(WorkflowSubprocessTimeoutSql.Find, P(call, ("CallId", call.Id)), token);

    /// <summary>返回空值才允许普通成功路径；截止封口与结果接纳竞争同一父根锁。</summary>
    private async Task<bool?> ProcessTimeoutAsync(SubprocessContext context, CancellationToken token)
    {
        if (context.Policy.Timeout is not { } policy) return null;
        var row = await ReadTimeoutAsync(context.Call, token).ConfigureAwait(false);
        if (row is null || row.PauseModeKey != policy.PauseModeKey) return await FaultAsync(context.Call, "workflow.subprocess.binding_invalid", token).ConfigureAwait(false);
        if (context.Parent.StatusKey is "cancelled" or "rejected" ||
            await query.QuerySingleOrDefaultAsync<long?>(WorkflowSubprocessSql.ActiveStepRevision, P(context.Call, ("Id", context.Call.Id)), token).ConfigureAwait(false) is null)
        {
            if (row.StatusKey is "waiting" or "closing" or "ready")
                return await ChangeTimeoutAsync(context.Call, row, "cancelled", token).ConfigureAwait(false);
            return null;
        }
        if (row.StatusKey is "closing" or "ready") return await CloseTimeoutAsync(context, row, token).ConfigureAwait(false);
        if (row.StatusKey != "waiting") return await FaultAsync(context.Call, "workflow.subprocess.binding_invalid", token).ConfigureAwait(false);
        if (context.Call.StatusKey == "result_ready" || context.Set?.StatusKey is "closing" or "result_ready") return null;
        if (context.Parent.StatusKey != "active" || row.PausedAtUtc is not null || row.DueAtUtc > clock.UtcNow) return null;
        var sealedResult = await transaction.ExecuteResultAsync(async inner => {
            var state = await LockAsync(context.Call, inner).ConfigureAwait(false); if (state is null) return Invalid();
            var (root, call) = state.Value; var current = await ReadTimeoutAsync(call, inner).ConfigureAwait(false); var set = await ReadSetAsync(call, inner).ConfigureAwait(false);
            if (root.StatusKey != "active" || call.StatusKey is not ("requested" or "running") || call.CancellationRequested ||
                current != row || current.PausedAtUtc is not null || current.DueAtUtc > clock.UtcNow || set?.StatusKey is "closing" or "result_ready" ||
                await query.QuerySingleOrDefaultAsync<long?>(WorkflowSubprocessSql.ActiveStepRevision, P(call, ("Id", call.Id)), inner).ConfigureAwait(false) is null) return Result<bool>.Success(false);
            if (!await WorkflowSubprocessTimeoutLifecycle.UpdateAsync(current with { StatusKey = "closing" }, commands, inner).ConfigureAwait(false) ||
                !await SetStatusAsync(call, call.StatusKey, true, null, inner).ConfigureAwait(false)) return Invalid();
            return Result<bool>.Success(true);
        }, token).ConfigureAwait(false);
        return sealedResult.IsSuccess && sealedResult.Value;
    }

    private async Task<bool> ChangeTimeoutAsync(WorkflowSubprocessCallRecord call, WorkflowSubprocessTimeoutRecord row, string status, CancellationToken token)
    {
        var result = await transaction.ExecuteResultAsync(async inner => {
            var state = await LockAsync(call, inner).ConfigureAwait(false); if (state is null) return Invalid();
            var current = await ReadTimeoutAsync(call, inner).ConfigureAwait(false);
            if (current != row) return Result<bool>.Success(false);
            if (status == "cancelled" && state.Value.Root.StatusKey is not ("cancelled" or "rejected") &&
                await query.QuerySingleOrDefaultAsync<long?>(WorkflowSubprocessSql.ActiveStepRevision, P(call, ("Id", call.Id)), inner).ConfigureAwait(false) is not null) return Result<bool>.Success(false);
            return await WorkflowSubprocessTimeoutLifecycle.UpdateAsync(row with { StatusKey = status }, commands, inner).ConfigureAwait(false) ? Result<bool>.Success(true) : Invalid();
        }, token).ConfigureAwait(false);
        return result.IsSuccess && result.Value;
    }

    private async Task<bool> CloseTimeoutAsync(SubprocessContext context, WorkflowSubprocessTimeoutRecord row, CancellationToken token)
    {
        var children = new Dictionary<Guid, WorkflowInstanceRecord>();
        foreach (var item in context.Items.Where(item => item.ChildInstanceId is not null))
        {
            var child = await ReadChildAsync(context with { Item = item }, token).ConfigureAwait(false);
            if (child is null) return await SuspendTimeoutAsync(context.Call, "workflow.subprocess.binding_invalid", token).ConfigureAwait(false);
            if (child.StatusKey is "active" or "suspended")
                return (await instances.CancelFromParentAsync(child.Id, new(context.Parent.Id, context.Call.Id, item.Id, item.InputHash), token).ConfigureAwait(false)).IsSuccess;
            if (!await ChildSettledAsync(child, context.Call, token).ConfigureAwait(false))
                return await SuspendTimeoutAsync(context.Call, "workflow.subprocess.child_outcome_unknown", token).ConfigureAwait(false);
            children.Add(item.Id, child);
        }
        var snapshot = context.Parent.StatusKey == "active" ? await outputs.ReadSnapshotAsync(context.Parent, context.Call.ParallelJoinId, context.Call.ParallelBranchKey, context.ParentSchema, token).ConfigureAwait(false) : null;
        if (context.Parent.StatusKey == "active" && snapshot is null) return await SuspendTimeoutAsync(context.Call, "workflow.subprocess.output_conflict", token).ConfigureAwait(false);
        var result = await transaction.ExecuteResultAsync(async inner => {
            var state = await LockAsync(context.Call, inner).ConfigureAwait(false); if (state is null) return Invalid();
            var (root, call) = state.Value; var current = await ReadTimeoutAsync(call, inner).ConfigureAwait(false);
            if (current != row || !call.CancellationRequested || root.StatusKey is not ("active" or "suspended")) return Result<bool>.Success(false);
            var items = await query.QueryAsync<WorkflowSubprocessItemRecord>(WorkflowSubprocessSql.Items, P(call, ("CallId", call.Id)), inner).ConfigureAwait(false);
            if (items.Count != context.Items.Count || items.Any(item => !context.Items.Contains(item))) return Result<bool>.Success(false);
            foreach (var item in items)
            {
                if (item.ChildInstanceId is { } id)
                {
                    if (!children.TryGetValue(item.Id, out var observed)) return Invalid();
                    var child = await ReadRootAsync(id, call.TenantScopeKey, inner).ConfigureAwait(false);
                    if (child != observed || child.StatusKey is "active" or "suspended" || !await ChildSettledAsync(child, call, inner).ConfigureAwait(false)) return Result<bool>.Success(false);
                }
                if (item.StatusKey is "pending" or "running" && await commands.ExecuteAsync(WorkflowSubprocessSql.RecordItemResult, P(call,
                        ("Id", item.Id), ("CallId", call.Id), ("Revision", item.Revision), ("StatusKey", "cancelled"), ("ResultJson", null),
                        ("ResultHash", null), ("ChildRevision", children.GetValueOrDefault(item.Id)?.Revision), ("Now", clock.UtcNow)), inner).ConfigureAwait(false) != 1) return Invalid();
            }
            if (root.StatusKey == "suspended")
            {
                if (row.StatusKey == "ready" && call.ErrorCode is null) return Result<bool>.Success(false);
                if (!await WorkflowSubprocessTimeoutLifecycle.UpdateAsync(row with { StatusKey = "ready" }, commands, inner).ConfigureAwait(false) ||
                    !await SetStatusAsync(call, "running", true, null, inner).ConfigureAwait(false)) return Invalid();
                return Result<bool>.Success(true);
            }
            var latest = await outputs.ReadSnapshotAsync(root, call.ParallelJoinId, call.ParallelBranchKey, context.ParentSchema, inner).ConfigureAwait(false);
            if (latest is null || snapshot is null || latest.Hash != snapshot.Hash || latest.FormRevision != snapshot.FormRevision) return Result<bool>.Success(false);
            var revision = await query.QuerySingleOrDefaultAsync<long?>(WorkflowSubprocessSql.ActiveStepRevision, P(call, ("Id", call.Id)), inner).ConfigureAwait(false);
            if (revision is null || await commands.ExecuteAsync(WorkflowSql.CompleteStepWithRevision, WorkflowSqlParameters.Create(("Id", call.Id),
                    ("InstanceId", root.Id), ("Revision", revision), ("StatusKey", "completed"), ("CompletedAtUtc", clock.UtcNow)), inner).ConfigureAwait(false) != 1) return Invalid();
            var set = await ReadSetAsync(call, inner).ConfigureAwait(false);
            if (set is not null && !await UpdateSetAsync(set with { StatusKey = "cancelled" }, inner).ConfigureAwait(false)) return Invalid();
            if (!await WorkflowSubprocessTimeoutLifecycle.UpdateAsync(row with { StatusKey = "applied" }, commands, inner).ConfigureAwait(false) ||
                !await SetStatusAsync(call, "applied", false, null, inner).ConfigureAwait(false)) return Invalid();
            var sequence = await query.QuerySingleOrDefaultAsync<long>(WorkflowSql.FindNextStepExecutionSequence, WorkflowSqlParameters.Create(("InstanceId", root.Id)), inner).ConfigureAwait(false);
            await scheduler.EnqueueAsync(root, await WorkflowExecutionEpochScope.ReadForStepAsync(query, root.Id, root.TenantScopeKey, call.Id, inner).ConfigureAwait(false) ?? Guid.Empty, context.Policy.Timeout!.NextNodeKey ?? call.ContinuationNodeKey, latest.Values, latest.FormRevision,
                sequence, call.ParallelJoinId, call.ParallelBranchKey, clock.UtcNow, inner, sourceStepId: call.Id).ConfigureAwait(false);
            if (await commands.ExecuteAsync(WorkflowSql.AdvanceInstanceWithRevision, WorkflowSqlParameters.Create(("Id", root.Id), ("TenantScopeKey", root.TenantScopeKey), ("Revision", root.Revision)), inner).ConfigureAwait(false) != 1) return Invalid();
            await commands.ExecuteAsync(WorkflowSql.InsertDomainAudit, WorkflowSqlParameters.Create(("Id", ids.NewId()), ("TenantId", root.TenantId), ("ScopeKey", root.ScopeKey),
                ("InstanceId", root.Id), ("OperationKey", "activity.execute"), ("ActorUserId", null), ("ResourceTypeKey", "step"), ("ResourceId", call.Id),
                ("OutcomeKey", "succeeded"), ("DetailJson", "{\"nodeTypeKey\":\"process.subprocess\",\"phaseKey\":\"timeout_applied\"}"),
                ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()), ("CreatedAtUtc", clock.UtcNow)), inner).ConfigureAwait(false);
            return Result<bool>.Success(true);
        }, token).ConfigureAwait(false);
        return result.IsSuccess && result.Value;
    }

    private async Task<bool> SuspendTimeoutAsync(WorkflowSubprocessCallRecord candidate, string code, CancellationToken token)
    {
        var result = await transaction.ExecuteResultAsync(async inner => {
            var state = await LockAsync(candidate, inner).ConfigureAwait(false); if (state is null) return Invalid();
            var (root, call) = state.Value; var timeout = await ReadTimeoutAsync(call, inner).ConfigureAwait(false);
            if (timeout?.StatusKey is not ("closing" or "ready") || root.StatusKey is not ("active" or "suspended")) return Result<bool>.Success(false);
            if (call.ErrorCode == code && root.StatusKey == "suspended") return Result<bool>.Success(false);
            if (!await SetStatusAsync(call, "faulted", true, code, inner).ConfigureAwait(false) || root.StatusKey == "active" &&
                !await SuspendAsync(root, inner).ConfigureAwait(false)) return Invalid();
            if (root.StatusKey == "active")
            {
                await commands.ExecuteAsync(database.Value.Provider == DatabaseProvider.SqlServer ? WorkflowDelaySql.PauseRelativeSqlServer : WorkflowDelaySql.PauseRelativeMySql,
                    WorkflowSqlParameters.Create(("InstanceId", root.Id), ("TenantScopeKey", root.TenantScopeKey), ("Now", clock.UtcNow)), inner).ConfigureAwait(false);
                if (!await WorkflowWorkingDelayLifecycle.ApplyAsync(root.Id, root.TenantScopeKey, "pause", clock.UtcNow, database.Value.Provider, query, commands, inner).ConfigureAwait(false) ||
                    !await WorkflowSlaLifecycle.ApplyAsync(root.Id, root.TenantScopeKey, "pause", clock.UtcNow, database.Value.Provider, query, commands, inner).ConfigureAwait(false) ||
                    !await WorkflowSubprocessTimeoutLifecycle.ApplyAsync(root.Id, root.TenantScopeKey, "pause", clock.UtcNow, query, commands, inner).ConfigureAwait(false)) return Invalid();
            }
            var detail = WorkflowJsonCanonicalizer.Compile(writer => { writer.WriteStartObject(); writer.WriteString("nodeTypeKey", WorkflowSubprocessPolicy.NodeType);
                writer.WriteString("phaseKey", "timeout_blocked"); writer.WriteString("errorCode", code); writer.WriteEndObject(); }).CanonicalJson;
            await commands.ExecuteAsync(WorkflowSql.InsertDomainAudit, WorkflowSqlParameters.Create(("Id", ids.NewId()), ("TenantId", root.TenantId), ("ScopeKey", root.ScopeKey),
                ("InstanceId", root.Id), ("OperationKey", "activity.execute"), ("ActorUserId", null), ("ResourceTypeKey", "step"), ("ResourceId", call.Id),
                ("OutcomeKey", "failed"), ("DetailJson", detail), ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),
                ("CreatedAtUtc", clock.UtcNow)), inner).ConfigureAwait(false);
            return Result<bool>.Success(true);
        }, token).ConfigureAwait(false);
        return result.IsSuccess && result.Value;
    }
}
