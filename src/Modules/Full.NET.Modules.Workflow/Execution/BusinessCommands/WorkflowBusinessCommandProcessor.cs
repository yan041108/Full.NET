using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Execution.BusinessCommands;

/// <summary>只处理Workflow自己的短事务：真实效果后续办、失败根请求补偿，绝不在事务中执行业务所有者Port。</summary>
internal sealed class WorkflowBusinessCommandProcessor(IQueryExecutor query, ICommandExecutor commands, ICommandTransaction transaction,
    ICurrentTenantContextWriter tenantWriter, IActiveTenantContextResolver tenants, IOptions<DatabaseOptions> database,
    IOptions<WorkflowRecoveryWorkerOptions> options, IClock clock, IIdGenerator ids, WorkflowBusinessActionCatalog catalog,
    WorkflowBusinessCommandPublisher publisher, WorkflowActivityScheduler scheduler, WorkflowAutomaticOutputCoordinator outputs,
    WorkflowBusinessCommandScanCursor cursors)
{
    public async Task<int> ProcessPendingAsync(CancellationToken token)
    {
        var processed = 0;
        foreach (var compensating in new[] { true, false })
        {
            var take = Math.Clamp(options.Value.BatchSize, 1, 8); var cursor = cursors.Read(compensating);
            tenantWriter.SetHost(); IReadOnlyList<WorkflowBusinessCommandRecord> rows;
            try {
                var sql = compensating ? database.Value.Provider == DatabaseProvider.SqlServer ? WorkflowBusinessCommandSql.CompensationsSqlServer : WorkflowBusinessCommandSql.CompensationsMySql
                    : database.Value.Provider == DatabaseProvider.SqlServer ? WorkflowBusinessCommandSql.ReadySqlServer : WorkflowBusinessCommandSql.ReadyMySql;
                rows = await query.QueryAsync<WorkflowBusinessCommandRecord>(sql, WorkflowSqlParameters.Create(("Take", take), ("HasAfter", cursor.Id is null ? 0 : 1),
                    ("AfterUpdatedAtUtc", cursor.UpdatedAtUtc ?? clock.UtcNow), ("AfterId", cursor.Id)), token).ConfigureAwait(false);
            } finally { tenantWriter.Clear(); }
            cursors.Advance(compensating, rows, take);
            foreach (var row in rows)
            {
                var instance = await ReadInstanceAsync(row, token).ConfigureAwait(false);
                if (instance is null || instance.TenantScopeKey != (instance.TenantId is { } id ? $"tenant:{id:N}" : "host") ||
                    instance.ScopeKey != (instance.TenantId is null ? "host" : "tenant")) continue;
                if (instance.TenantId is { } tenantId) {
                    var tenant = await tenants.ResolveActiveByIdAsync(tenantId, token).ConfigureAwait(false);
                    if (tenant is null) continue; tenantWriter.SetTenant(tenant);
                } else tenantWriter.SetHost();
                try {
                    var result = await transaction.ExecuteResultAsync(inner => ExecuteAsync(row, compensating, inner), token).ConfigureAwait(false);
                    if (result.IsSuccess && result.Value) processed++;
                } finally { tenantWriter.Clear(); }
            }
        }
        return processed;
    }
    private Task<WorkflowInstanceRecord?> ReadInstanceAsync(WorkflowBusinessCommandRecord row, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", row.InstanceId), ("TenantScopeKey", row.TenantScopeKey)), token);
    private async Task<Result<bool>> ExecuteAsync(WorkflowBusinessCommandRecord candidate, bool compensating, CancellationToken token)
    {
        if (await commands.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction,
                WorkflowSqlParameters.Create(("Id", candidate.InstanceId), ("TenantScopeKey", candidate.TenantScopeKey)), token).ConfigureAwait(false) != 1) return Invalid();
        var instance = await ReadInstanceAsync(candidate, token).ConfigureAwait(false);
        var row = await query.QuerySingleOrDefaultAsync<WorkflowBusinessCommandRecord>(WorkflowBusinessCommandSql.Find,
            WorkflowSqlParameters.Create(("Id", candidate.Id), ("InstanceId", candidate.InstanceId), ("TenantScopeKey", candidate.TenantScopeKey)), token).ConfigureAwait(false);
        if (instance is null || row is null) return Invalid();
        if (compensating ? instance.StatusKey is not ("rejected" or "cancelled") || row.CompensationRequested || row.StatusKey is "cancelled" or "compensated" or "declined"
            : instance.StatusKey != "active" || row.StatusKey != "result_ready") return Result<bool>.Success(false);
        var binding = WorkflowBusinessCommandValidation.Read(row, instance, catalog);
        if (binding is null) return await FaultAsync(instance, row, token).ConfigureAwait(false);
        if (compensating)
        {
            if (row.Generation >= 1000) return await FaultAsync(instance, row, token).ConfigureAwait(false);
            if (await commands.ExecuteAsync(WorkflowBusinessCommandSql.BeginCompensation, WorkflowSqlParameters.Create(("Id", row.Id),
                    ("InstanceId", instance.Id), ("TenantScopeKey", row.TenantScopeKey), ("Revision", row.Revision), ("Now", clock.UtcNow)), token).ConfigureAwait(false) != 1) return Invalid();
            await publisher.RequestAsync(binding, row.InputHash, "compensate", row.Generation + 1, clock.UtcNow, token).ConfigureAwait(false);
            return Result<bool>.Success(true);
        }
        var asset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId), ("TenantScopeKey", row.TenantScopeKey)), token).ConfigureAwait(false);
        WorkflowDefinitionDraft? draft = null; WorkflowFormSchema? schema = null;
        try { if (asset is not null) {
            draft = JsonSerializer.Deserialize(asset.CanonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
            schema = JsonSerializer.Deserialize(asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        } } catch (JsonException) { }
        if (draft is null || schema is null || !WorkflowRuntimePlan.TryCreate(draft, schema, out _) ||
            !WorkflowBusinessCommandValidation.MatchesDefinition(row, draft) ||
            !draft.Nodes.Any(node => node.NodeKey == row.ContinuationNodeKey && node.NodeTypeKey == "end"))
            return await FaultAsync(instance, row, token).ConfigureAwait(false);
        var snapshot = await outputs.ReadSnapshotAsync(instance, null, null, schema, token).ConfigureAwait(false);
        if (snapshot is null) return await FaultAsync(instance, row, token).ConfigureAwait(false);
        var revision = await query.QuerySingleOrDefaultAsync<long?>(WorkflowBusinessCommandSql.ActiveStepRevision,
            WorkflowSqlParameters.Create(("Id", row.Id), ("InstanceId", instance.Id), ("TenantScopeKey", row.TenantScopeKey)), token).ConfigureAwait(false);
        if (revision is null || await commands.ExecuteAsync(WorkflowSql.CompleteStepWithRevision,
                WorkflowSqlParameters.Create(("Id", row.Id), ("InstanceId", instance.Id), ("Revision", revision.Value),
                    ("StatusKey", "completed"), ("CompletedAtUtc", clock.UtcNow)), token).ConfigureAwait(false) != 1) return Invalid();
        if (await commands.ExecuteAsync(WorkflowBusinessCommandSql.Complete, WorkflowSqlParameters.Create(("Id", row.Id), ("InstanceId", instance.Id),
                ("TenantScopeKey", row.TenantScopeKey), ("Revision", row.Revision), ("Now", clock.UtcNow)), token).ConfigureAwait(false) != 1) return Invalid();
        var sequence = await query.QuerySingleOrDefaultAsync<long>(WorkflowSql.FindNextStepExecutionSequence,
            WorkflowSqlParameters.Create(("InstanceId", instance.Id)), token).ConfigureAwait(false);
        await scheduler.EnqueueAsync(instance, await WorkflowExecutionEpochScope.ReadForStepAsync(query, instance.Id, instance.TenantScopeKey, row.Id, token).ConfigureAwait(false) ?? Guid.Empty, row.ContinuationNodeKey, snapshot.Values, snapshot.FormRevision, sequence, null, null, clock.UtcNow, token, sourceStepId: row.Id).ConfigureAwait(false);
        if (await commands.ExecuteAsync(WorkflowSql.AdvanceInstanceWithRevision, WorkflowSqlParameters.Create(("Id", instance.Id),
                ("TenantScopeKey", row.TenantScopeKey), ("Revision", instance.Revision)), token).ConfigureAwait(false) != 1) return Invalid();
        await commands.ExecuteAsync(WorkflowSql.InsertExecutionLog, WorkflowSqlParameters.Create(("Id", ids.NewId()), ("InstanceId", instance.Id),
            ("StepId", row.Id), ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),
            ("TransitionKey", "node.business.command.applied"), ("FromStatusKey", "active"), ("ToStatusKey", "completed"),
            ("IdempotencyKey", null), ("Summary", null), ("CreatedAtUtc", clock.UtcNow)), token).ConfigureAwait(false);
        return Result<bool>.Success(true);
    }
    private async Task<Result<bool>> FaultAsync(WorkflowInstanceRecord instance, WorkflowBusinessCommandRecord row, CancellationToken token)
    {
        if (await commands.ExecuteAsync(WorkflowBusinessCommandSql.Fault, WorkflowSqlParameters.Create(("Id", row.Id), ("InstanceId", instance.Id),
                ("TenantScopeKey", row.TenantScopeKey), ("Revision", row.Revision), ("Now", clock.UtcNow)), token).ConfigureAwait(false) != 1) return Invalid();
        if (instance.StatusKey == "active" && await commands.ExecuteAsync(WorkflowSql.SuspendInstanceWithRevision,
                WorkflowSqlParameters.Create(("Id", instance.Id), ("TenantScopeKey", row.TenantScopeKey), ("Revision", instance.Revision)), token).ConfigureAwait(false) != 1) return Invalid();
        return Result<bool>.Success(true);
    }
    private static Result<bool> Invalid() => Result<bool>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
}
