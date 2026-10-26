using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Execution.Connectors;

/// <summary>分发、远端响应和表单接纳分阶段持久化；任何网络调用都不持有Workflow事务。</summary>
internal sealed partial class WorkflowConnectorProcessor(IQueryExecutor query, ICommandExecutor commands,
    ICommandTransaction transaction, IClock clock, IIdGenerator ids, ICurrentTenantContextWriter tenants,
    IOptions<DatabaseOptions> database, IOptions<WorkflowRecoveryWorkerOptions> options,
    WorkflowConnectorCatalog catalog, WorkflowConnectorHttpClient client, WorkflowAutomaticOutputCoordinator outputs,
    WorkflowActivityScheduler scheduler)
{
    public async Task<int> ProcessPendingAsync(CancellationToken token)
    {
        var applyStatement = database.Value.Provider == DatabaseProvider.SqlServer ? WorkflowConnectorSql.ListApplySqlServer : WorkflowConnectorSql.ListApplyMySql;
        var ready = await query.QueryAsync<WorkflowConnectorCallRecord>(applyStatement,
            Parameters(("Now", clock.UtcNow), ("Take", Math.Min(8, options.Value.BatchSize))), token).ConfigureAwait(false);
        var progressed = 0;
        foreach (var call in ready) if (await ApplyAsync(call, token).ConfigureAwait(false)) progressed++;
        var dueStatement = database.Value.Provider == DatabaseProvider.SqlServer ? WorkflowConnectorSql.ListDispatchSqlServer : WorkflowConnectorSql.ListDispatchMySql;
        var due = await query.QueryAsync<WorkflowConnectorCallRecord>(dueStatement,
            Parameters(("Now", clock.UtcNow), ("Take", Math.Min(2, options.Value.BatchSize))), token).ConfigureAwait(false);
        foreach (var call in due)
        {
            token.ThrowIfCancellationRequested();
            if (call.StatusKey == "dispatched") { if (await RecoverAsync(call, token).ConfigureAwait(false)) progressed++; continue; }
            if (await DispatchAsync(call, token).ConfigureAwait(false)) progressed++;
        }
        return progressed;
    }

    private async Task<bool> DispatchAsync(WorkflowConnectorCallRecord call, CancellationToken token)
    {
        var instance = await FindInstanceAsync(call, token).ConfigureAwait(false);
        if (!Trusted(instance, call) || instance!.StatusKey != "active") return false;
        using var tenantScope = new WorkflowExecutionTenantScope(tenants, instance.TenantId);
        var runtime = await LoadRuntimeAsync(instance, call, token).ConfigureAwait(false);
        if (runtime is null) return await FaultPendingAsync(call, "workflow.connector.binding_invalid", token).ConfigureAwait(false);
        var owner = ids.NewId().ToString("N"); var attemptId = ids.NewId();
        var claimed = await transaction.ExecuteResultAsync(async inner =>
        {
            var current = await FindInstanceAsync(call, inner).ConfigureAwait(false);
            if (!Trusted(current, call) || current!.StatusKey != "active" || !await LockAsync(current, inner).ConfigureAwait(false)) return Invalid<WorkflowConnectorCallRecord?>();
            var currentAsync = call.ModeKey=="async" ? await outputs.ReadSnapshotAsync(current,call.ParallelJoinId,call.ParallelBranchKey,runtime.Schema,inner).ConfigureAwait(false) : null;
            if (call.ModeKey=="sync" && !await outputs.MatchesCurrentAsync(current, Input(call), runtime.Values, inner).ConfigureAwait(false) ||
                call.ModeKey=="async" && (currentAsync is null || !WorkflowAsyncOutputPolicy.TargetsUnchanged(runtime.Policy,runtime.Values,currentAsync.Values)))
            {
                if (!await FailPendingCoreAsync(current, call, "failed", "workflow.connector.input_conflict", inner).ConfigureAwait(false)) return Invalid<WorkflowConnectorCallRecord?>();
                return Result<WorkflowConnectorCallRecord?>.Success(null);
            }
            if (call.AttemptCount >= runtime.Connector.Definition.MaximumAttempts)
            {
                if (!await FailPendingCoreAsync(current, call, "outcome_unknown", "workflow.connector.retry_exhausted", inner).ConfigureAwait(false)) return Invalid<WorkflowConnectorCallRecord?>();
                return Result<WorkflowConnectorCallRecord?>.Success(null);
            }
            var now = clock.UtcNow;
            var expires = now.AddSeconds(Math.Max(options.Value.LeaseSeconds, runtime.Connector.Definition.TimeoutSeconds + 30));
            if (await commands.ExecuteAsync(WorkflowConnectorSql.ClaimDispatch,
                    Parameters(("Id", call.Id), ("InstanceId", call.InstanceId), ("TenantScopeKey", call.TenantScopeKey), ("Revision", call.Revision),
                        ("AttemptId", attemptId), ("Owner", owner), ("Expires", expires), ("Now", now), ("MaximumAttempts", runtime.Connector.Definition.MaximumAttempts)), inner).ConfigureAwait(false) != 1)
                return Invalid<WorkflowConnectorCallRecord?>();
            await commands.ExecuteAsync(WorkflowConnectorSql.InsertAttempt,
                Parameters(("AttemptId", attemptId), ("Id", call.Id), ("TenantScopeKey", call.TenantScopeKey),
                    ("Generation", call.LeaseGeneration + 1), ("Owner", owner), ("RequestHash", call.RequestHash), ("Now", now)), inner).ConfigureAwait(false);
            await AuditAsync(current, call, "dispatched", attemptId, null, inner).ConfigureAwait(false);
            return Result<WorkflowConnectorCallRecord?>.Success(call with { StatusKey = "dispatched", CurrentAttemptId = attemptId,
                LeaseGeneration = call.LeaseGeneration + 1, LeaseOwnerKey = owner, LeaseExpiresAtUtc = expires, AttemptCount = call.AttemptCount + 1, Revision = call.Revision + 1 });
        }, token).ConfigureAwait(false);
        if (!claimed.IsSuccess) return false;
        if (claimed.Value is not { } dispatched) return true;
        WorkflowConnectorCallResult response;
        try { response = await client.SendAsync(runtime.Connector, dispatched.Id, dispatched.RequestJson, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { response = new("retryable", null, null, "workflow.connector.cancelled_before_send"); }
        // 即使宿主取消，仍尝试保存本次响应；无法确认提交时保留原分发事实供租约恢复对账。
        await ReceiveAsync(dispatched, runtime.Connector, response, CancellationToken.None).ConfigureAwait(false);
        var captured = await FindAsync(call.Id, call.TenantScopeKey, CancellationToken.None).ConfigureAwait(false);
        if (captured is { StatusKey:"result_ready",ModeKey:"sync" } && !token.IsCancellationRequested) _ = await ApplyAsync(captured, token).ConfigureAwait(false);
        return true;
    }

    private async Task<ConnectorRuntime?> LoadRuntimeAsync(WorkflowInstanceRecord instance, WorkflowConnectorCallRecord call, CancellationToken token)
    {
        if(call.ModeKey is not("sync" or "async"))return null;
        if (Hash(call.InputJson) != call.InputHash || Hash(call.RequestJson) != call.RequestHash) return null;
        var asset = await query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            Parameters(("DefinitionVersionId", instance.DefinitionVersionId), ("TenantScopeKey", instance.TenantScopeKey)), token).ConfigureAwait(false);
        if (asset is null || instance.FormVersionId != asset.FormVersionId || !WorkflowDesignerRelease.AllowsExecution(
                await WorkflowDesignerRelease.ReadBindingsAsync(query, asset.FormVersionId,
                    new WorkflowManagementScope(instance.TenantId, instance.ScopeKey, instance.TenantScopeKey), token).ConfigureAwait(false))) return null;
        try
        {
            var draft = JsonSerializer.Deserialize(asset.CanonicalJson, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
            var schema = JsonSerializer.Deserialize(asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
            var values = JsonSerializer.Deserialize(call.InputJson, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
            if (draft is null || schema is null || values is null || !WorkflowRuntimePlan.TryCreate(draft, schema, out var plan) ||
                !plan!.TryResolveContinuation(call.NodeKey, values, null, out var transition,call.NodeKey) || transition.AutomaticWait is not WorkflowConnectorWaitPlan wait ||
                wait.NodeKey != call.NodeKey || wait.ContinuationNodeKey != call.ContinuationNodeKey || wait.ConnectorHash != call.ConnectorHash ||
                wait.Policy.ConnectorKey != call.ConnectorKey || wait.Policy.ConnectorVersion != call.ConnectorVersion || wait.IsAsync!=(call.ModeKey=="async") ||
                !wait.Policy.TryCreateInput(values, out var request) || request != call.RequestJson ||
                !catalog.CanBind(instance.TenantScopeKey, wait.Policy, out var connector) || connector!.ContentHash != call.ConnectorHash) return null;
            if(plan.TryGetAsyncWait(call.NodeKey,out var dependency))
            {
                var prerequisites=await query.QueryAsync<WorkflowConnectorCallRecord>(WorkflowConnectorSql.FindInFrame,
                    Parameters(("InstanceId",instance.Id),("TenantScopeKey",instance.TenantScopeKey),("NodeKeys",dependency!.ProducerNodeKeys.ToArray()),
                        ("ParallelJoinId",call.ParallelJoinId),("ParallelBranchKey",call.ParallelBranchKey)),token).ConfigureAwait(false);
                if(prerequisites.Count!=dependency.ProducerNodeKeys.Count || prerequisites.GroupBy(item=>item.NodeKey,StringComparer.Ordinal).Any(group=>group.Count()!=1) ||
                    prerequisites.Any(item=>item.StatusKey!="applied"))return null;
            }
            return new(schema, values, wait.Policy, connector);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException) { return null; }
    }

    private Task<WorkflowConnectorCallRecord?> FindAsync(Guid id, string scope, CancellationToken token) => query.QuerySingleOrDefaultAsync<WorkflowConnectorCallRecord>(
        WorkflowConnectorSql.Find, Parameters(("Id", id), ("TenantScopeKey", scope)), token);
    private Task<WorkflowInstanceRecord?> FindInstanceAsync(WorkflowConnectorCallRecord call, CancellationToken token) => query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
        WorkflowSql.FindInstanceById, Parameters(("Id", call.InstanceId), ("TenantScopeKey", call.TenantScopeKey)), token);
    private async Task<bool> LockAsync(WorkflowInstanceRecord instance, CancellationToken token) => await commands.ExecuteAsync(WorkflowConnectorSql.LockInstance,
        Parameters(("Id", instance.Id), ("TenantScopeKey", instance.TenantScopeKey), ("Revision", instance.Revision)), token).ConfigureAwait(false) == 1;
    private static bool Trusted(WorkflowInstanceRecord? instance, WorkflowConnectorCallRecord call) => instance is not null && instance.Id == call.InstanceId &&
        instance.TenantScopeKey == call.TenantScopeKey && instance.TenantScopeKey == (instance.TenantId is { } id ? $"tenant:{id:N}" : "host") &&
        instance.ScopeKey == (instance.TenantId is null ? "host" : "tenant");
    private static WorkflowAutomaticInput Input(WorkflowConnectorCallRecord call) => new(call.Id, call.InputJson, call.InputHash, call.InputFormRevision, call.ParallelJoinId, call.ParallelBranchKey);
    private static string Hash(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    private static IReadOnlyDictionary<string, object?> Parameters(params (string Key, object? Value)[] values) => WorkflowSqlParameters.Create(values);
    private static Result<T> Invalid<T>() => Result<T>.Failure(WorkflowTransitionExecutionErrors.InvalidTransition());
    private sealed record ConnectorRuntime(WorkflowFormSchema Schema, IReadOnlyDictionary<string, JsonElement> Values,
        WorkflowConnectorPolicy Policy, WorkflowBoundHttpConnector Connector);
}
