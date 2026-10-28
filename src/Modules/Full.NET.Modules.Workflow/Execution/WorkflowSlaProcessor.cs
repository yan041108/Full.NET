using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>截止处置只含实例锁内的本地事实；不伪造个人办理或在事务内调用外部服务。</summary>
internal sealed class WorkflowSlaProcessor(IQueryExecutor query, ICommandExecutor commands, ICommandTransaction transaction,
    IClock clock, IIdGenerator ids, IOptions<DatabaseOptions> database, IOptions<WorkflowRecoveryWorkerOptions> options,
    WorkflowActivityScheduler scheduler, WorkflowAutomaticOutputCoordinator outputs, WorkflowNotificationOutboxPublisher notifications,
    IActiveTenantContextResolver tenants, ICurrentTenantContextWriter tenantWriter, WorkflowSlaScanCursor scanCursor, WorkflowHumanActivityCompletionCoordinator callbacks)
{
    public async Task<int> ProcessPendingAsync(CancellationToken token)
    {
        var cursor = scanCursor.Read();
        tenantWriter.SetHost();
        IReadOnlyList<WorkflowSlaRecord> rows;
        try {
            rows = await query.QueryAsync<WorkflowSlaRecord>(database.Value.Provider == DatabaseProvider.SqlServer
                ? WorkflowSlaSql.ListDueSqlServer : WorkflowSlaSql.ListDueMySql,
                WorkflowSqlParameters.Create(("Now", clock.UtcNow), ("Take", options.Value.BatchSize), ("HasAfter", cursor.Id is null ? 0 : 1),
                    ("AfterDueAtUtc", cursor.DueAtUtc ?? clock.UtcNow), ("AfterId", cursor.Id)), token).ConfigureAwait(false);
        } finally { tenantWriter.Clear(); }
        scanCursor.Advance(rows, options.Value.BatchSize);
        var processed = 0;
        foreach (var candidate in rows)
        {
            var instance = await ReadInstanceAsync(candidate, token).ConfigureAwait(false);
            if (instance is null || instance.TenantScopeKey != (instance.TenantId is { } id ? $"tenant:{id:N}" : "host") ||
                instance.ScopeKey != (instance.TenantId is null ? "host" : "tenant")) continue;
            if (instance.TenantId is { } tenantId)
            {
                var tenant = await tenants.ResolveActiveByIdAsync(tenantId, token).ConfigureAwait(false);
                if (tenant is null) continue;
                tenantWriter.SetTenant(tenant);
            }
            else tenantWriter.SetHost();
            try {
                var result = await transaction.ExecuteResultAsync(inner => ExecuteAsync(candidate, inner), token).ConfigureAwait(false);
                if (result.IsSuccess && result.Value) processed++;
            } finally { tenantWriter.Clear(); }
        }
        return processed;
    }

    private Task<WorkflowInstanceRecord?> ReadInstanceAsync(WorkflowSlaRecord row, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", row.InstanceId), ("TenantScopeKey", row.TenantScopeKey)), token);

    private async Task<Result<bool>> ExecuteAsync(WorkflowSlaRecord candidate, CancellationToken token)
    {
        var anchor = WorkflowSqlParameters.Create(("Id", candidate.InstanceId), ("TenantScopeKey", candidate.TenantScopeKey));
        // 先锁实例再读步骤；人工提交、改派、暂停和截止使用一致锁顺序。
        if (await commands.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction, anchor, token).ConfigureAwait(false) != 1) return Invalid();
        var instance = await ReadInstanceAsync(candidate, token).ConfigureAwait(false);
        var row = await query.QuerySingleOrDefaultAsync<WorkflowSlaRecord>(WorkflowSlaSql.Find,
            WorkflowSqlParameters.Create(("Id", candidate.Id), ("InstanceId", candidate.InstanceId), ("TenantScopeKey", candidate.TenantScopeKey)), token).ConfigureAwait(false);
        if (instance is null || row is null || row.StatusKey != "waiting") return Result<bool>.Success(false);
        var now = clock.UtcNow;
        var closed = row.StepStatusKey != "active" || instance.StatusKey is not ("active" or "suspended");
        if (!closed && (instance.StatusKey != "active" || row.PausedAtUtc is not null || row.DueAtUtc > now)) return Result<bool>.Success(false);
        var parameters = WorkflowSqlParameters.Create(("Id", row.Id), ("StepId", row.Id), ("InstanceId", instance.Id),
            ("TenantScopeKey", instance.TenantScopeKey), ("Revision", row.Revision), ("Now", now), ("StatusKey", closed ? "cancelled" : "fired"));
        if (await commands.ExecuteAsync(WorkflowSlaSql.Finish, parameters, token).ConfigureAwait(false) != 1) return Invalid();
        if (closed) return Result<bool>.Success(true);

        var asset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId), ("TenantScopeKey", instance.TenantScopeKey)), token).ConfigureAwait(false);
        WorkflowDefinitionDraft? draft = null; WorkflowFormSchema? schema = null; WorkflowSlaPolicy? policy = null; WorkflowRuntimePlan? plan = null;
        try {
            if (asset is not null) {
                draft = JsonSerializer.Deserialize(asset.CanonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
                schema = JsonSerializer.Deserialize(asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
            }
        } catch (JsonException) { /* 损坏发布输入只允许挂起，不能继续或反复执行处置。 */ }
        var node = draft?.Nodes?.FirstOrDefault(item => item?.NodeKey == row.NodeKey);
        var valid = draft is not null && schema is not null && WorkflowRuntimePlan.TryCreate(draft, schema, out plan) && node is not null &&
            node.NodeTypeKey == row.NodeTypeKey && WorkflowSlaPolicy.TryRead(node.Config, node.NodeTypeKey, out policy) &&
            policy is not null && policy.ActionKey == row.ActionKey && policy.PauseModeKey == row.PauseModeKey;
        var action = valid ? row.ActionKey : "suspend";
        WorkflowAutomaticSnapshot? snapshot = null;
        string? next = null;
        if (action == "auto_pass")
        {
            snapshot = await outputs.ReadSnapshotAsync(instance, row.ParallelJoinId, row.ParallelBranchKey, schema!, token).ConfigureAwait(false);
            if (snapshot is null || !WorkflowHumanActivityKinds.IsApproval(row.NodeTypeKey) ||
                !node!.Config.TryGetProperty("nextNodeKeys", out var successors) || successors.ValueKind != JsonValueKind.Array ||
                successors.GetArrayLength() != 1 || successors[0].ValueKind != JsonValueKind.String ||
                !WorkflowNodeKeyValidator.IsValid(next = successors[0].GetString())) { action = "suspend"; valid = false; }
        }
        var continuationValues = snapshot?.Values; long? continuationRevision = snapshot?.FormRevision;
        if (valid && action is ("auto_pass" or "auto_reject"))
        {
            snapshot ??= await outputs.ReadSnapshotAsync(instance, row.ParallelJoinId, row.ParallelBranchKey, schema!, token).ConfigureAwait(false);
            if (snapshot is null) { action = "suspend"; valid = false; }
            else
            {
                var callback = await callbacks.ApplyAsync(instance, row.Id, row.NodeKey, plan!, snapshot.Values, action == "auto_pass", null,
                    row.ParallelJoinId, row.ParallelBranchKey, now, token).ConfigureAwait(false);
                if (!callback.IsSuccess) return Invalid();
                continuationValues = callback.Value!.Values; continuationRevision = callback.Value.FormRevision ?? snapshot.FormRevision;
            }
        }
        var root = WorkflowSqlParameters.Create(("Id", instance.Id), ("TenantScopeKey", instance.TenantScopeKey),
            ("Revision", instance.Revision), ("StatusKey", "rejected"), ("CompletedAtUtc", now));
        if (action == "suspend")
        {
            if (await commands.ExecuteAsync(WorkflowSql.SuspendInstanceWithRevision, root, token).ConfigureAwait(false) != 1) return Invalid();
            await commands.ExecuteAsync(database.Value.Provider == DatabaseProvider.SqlServer ? WorkflowDelaySql.PauseRelativeSqlServer : WorkflowDelaySql.PauseRelativeMySql,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey), ("Now", now)), token).ConfigureAwait(false);
            if (!await WorkflowWorkingDelayLifecycle.ApplyAsync(instance.Id, instance.TenantScopeKey, "pause", now, database.Value.Provider, query, commands, token).ConfigureAwait(false) ||
                !await WorkflowSlaLifecycle.ApplyAsync(instance.Id, instance.TenantScopeKey, "pause", now, database.Value.Provider, query, commands, token).ConfigureAwait(false) ||
                !await WorkflowSubprocessTimeoutLifecycle.ApplyAsync(instance.Id, instance.TenantScopeKey, "pause", now, query, commands, token).ConfigureAwait(false)) return Invalid();
        }
        else if (action == "auto_reject")
        {
            if (await commands.ExecuteAsync(WorkflowSql.CompleteInstanceWithRevision, root, token).ConfigureAwait(false) != 1) return Invalid();
            await WorkflowHumanActivityLifecycle.RejectAsync(commands, instance.Id, instance.TenantScopeKey, now, token).ConfigureAwait(false);
            await commands.ExecuteAsync(WorkflowActivitySql.CancelWork,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey), ("Now", now)), token).ConfigureAwait(false);
            await notifications.PublishInstanceRejectedAsync(instance.Id, instance.StartedById, instance.BusinessType, instance.BusinessId, now, token).ConfigureAwait(false);
        }
        else
        {
            await WorkflowHumanActivityLifecycle.CancelCountersignStepAsync(commands, instance.Id, row.Id, instance.TenantScopeKey, null, token).ConfigureAwait(false);
            await commands.ExecuteAsync(WorkflowSlaSql.CancelStepTodos, parameters, token).ConfigureAwait(false);
            await commands.ExecuteAsync(WorkflowSlaSql.CancelStepSlots, parameters, token).ConfigureAwait(false);
            await commands.ExecuteAsync(WorkflowGroupApprovalSql.CancelActiveByStep, parameters, token).ConfigureAwait(false);
            if (await commands.ExecuteAsync(WorkflowSql.CompleteStepWithRevision,
                    WorkflowSqlParameters.Create(("Id", row.Id), ("InstanceId", instance.Id), ("Revision", row.StepRevision),
                        ("StatusKey", "completed"), ("CompletedAtUtc", now)), token).ConfigureAwait(false) != 1) return Invalid();
            var sequence = await query.QuerySingleOrDefaultAsync<long>(WorkflowSql.FindNextStepExecutionSequence,
                WorkflowSqlParameters.Create(("InstanceId", instance.Id)), token).ConfigureAwait(false);
            await scheduler.EnqueueAsync(instance, await WorkflowExecutionEpochScope.ReadForStepAsync(query, instance.Id, instance.TenantScopeKey, row.Id, token).ConfigureAwait(false) ?? Guid.Empty, next!, continuationValues!, continuationRevision!.Value, sequence,
                row.ParallelJoinId, row.ParallelBranchKey, now, token, sourceStepId: row.Id).ConfigureAwait(false);
            if (await commands.ExecuteAsync(WorkflowSql.AdvanceInstanceWithRevision, root, token).ConfigureAwait(false) != 1) return Invalid();
        }
        var correlation = Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson();
        await commands.ExecuteAsync(WorkflowSql.InsertExecutionLog, WorkflowSqlParameters.Create(("Id", ids.NewId()), ("InstanceId", instance.Id),
            ("StepId", row.Id), ("CorrelationJson", correlation), ("TransitionKey", $"activity.sla.{action}"),
            ("FromStatusKey", "active"), ("ToStatusKey", action == "suspend" ? "suspended" : action == "auto_reject" ? "rejected" : "completed"),
            ("IdempotencyKey", null), ("Summary", valid ? null : "workflow.sla.input_invalid"), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        await commands.ExecuteAsync(WorkflowSql.InsertDomainAudit, WorkflowSqlParameters.Create(("Id", ids.NewId()), ("TenantId", instance.TenantId),
            ("ScopeKey", instance.ScopeKey), ("InstanceId", instance.Id), ("OperationKey", "activity.execute"), ("ActorUserId", null),
            ("ResourceTypeKey", "step"), ("ResourceId", row.Id), ("OutcomeKey", "succeeded"),
            ("DetailJson", $"{{\"sourceKey\":\"sla\",\"actionKey\":\"{action}\",\"publishedPolicyValid\":{(valid ? "true" : "false")}}}"),
            ("CorrelationJson", correlation), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
        return Result<bool>.Success(true);
    }
    private static Result<bool> Invalid() => Result<bool>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
}
