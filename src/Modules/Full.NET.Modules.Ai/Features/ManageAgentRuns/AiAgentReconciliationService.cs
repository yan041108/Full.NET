using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Features.ManageAgentRuns;

/// <summary>人工关闭原未知执行并补录核证计量；运行、账本和证据事件共享本地事务。</summary>
internal sealed class AiAgentReconciliationService(AiAgentRunStore runs, IQueryExecutor queries, ICommandExecutor commands,
    ICommandTransaction transaction, IAiOperationBudgetStore budget, ICurrentSessionAuthorization authorization,
    ICurrentTenant tenant, IClock clock, IIdGenerator ids, IIndependentCurrentSessionAuthorization independentAuthorization,
    IDataTransactionState transactionState)
{
    internal async Task<Result<AiAgentReconciliationResponse>> GetAsync(Guid runId, CancellationToken token)
    {
        RequireNoTransaction();
        var actor = await authorization.AuthorizeAsync(AiAgentRunPermissions.Read, token).ConfigureAwait(false);
        if (!Current(actor)) return Forbidden<AiAgentReconciliationResponse>();
        var scope = Scope();
        var run = await runs.FindOwnedAsync(runId, scope, actor!.UserId, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!Owned(run, runId, actor, scope)) return NotFound<AiAgentReconciliationResponse>();
        var operation = await OperationAsync(run!, token).ConfigureAwait(false);
        if (!Current(actor) || Scope() != scope) return Forbidden<AiAgentReconciliationResponse>();
        if (operation is null) return Conflict<AiAgentReconciliationResponse>();
        // 读取包含账本状态，等待后重新确认当前交互会话，不能沿用失效主体。
        var final = await authorization.AuthorizeAsync(AiAgentRunPermissions.Read, token).ConfigureAwait(false);
        if (final != actor || !Current(final)) return Forbidden<AiAgentReconciliationResponse>();
        return Result<AiAgentReconciliationResponse>.Success(new(run!.Id, run.Version, run.StatusKey, operation.UsageStatus,
            operation.ReservedTokens, operation.QuotaMonthKey, operation.InputTokens, operation.OutputTokens,
            operation.CachedInputTokens, run.StatusKey == "reconciliation_required" && operation.UsageStatus is "reserved" or "unknown" or "known"));
    }

    internal async Task<Result<bool>> ReconcileAsync(Guid runId, ReconcileAiAgentRunRequest request, CancellationToken token)
    {
        RequireNoTransaction();
        token.ThrowIfCancellationRequested();
        if (runId == Guid.Empty || !Valid(request)) return Result<bool>.Failure(new Error("ai.agent_reconciliation.invalid",
            "The reconciliation request is invalid.", ErrorType.Validation));
        var actor = await authorization.AuthorizeAsync(AiAgentRunPermissions.Reconcile, token).ConfigureAwait(false);
        if (!Current(actor)) return Forbidden<bool>();
        var scope = Scope();
        var run = await runs.FindOwnedAsync(runId, scope, actor!.UserId, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!Owned(run, runId, actor, scope)) return NotFound<bool>();
        var payload = JsonSerializer.Serialize(request, AiJsonSerializerContext.Default.ReconcileAiAgentRunRequest);
        if (run.StatusKey != "reconciliation_required")
        {
            // 只接受本次原证据的逐字节重放；不覆盖终态，不把其他处理键当作成功。
            var saved = await queries.QuerySingleOrDefaultAsync<string>(AiAgentRunSql.FindReconciliation,
                AiSqlParameters.Create(("RunId", runId), ("ScopeKey", scope), ("ActorUserId", actor.UserId)), token).ConfigureAwait(false);
            var replayActor = await authorization.AuthorizeAsync(AiAgentRunPermissions.Reconcile, token).ConfigureAwait(false);
            return run.StatusKey == "failed" && saved == payload && replayActor == actor && Current(replayActor)
                ? Result<bool>.Success(true) : Conflict<bool>();
        }
        if (run.Version != request.ExpectedVersion) return Conflict<bool>();
        // 无专用准入或实际池只有一个连接时，在任何业务写入前返回容量拒绝，不能等待自身事务。
        if (!independentAuthorization.HasIndependentReadCapacity)
            throw new ServiceCapacityExceededException(ServiceCapacityFailureKind.Rejected, TimeSpan.FromMilliseconds(250));
        var operation = await OperationAsync(run, token).ConfigureAwait(false);
        if (operation is null || operation.UsageStatus is not ("reserved" or "unknown" or "known")) return Conflict<bool>();
        if (operation.UsageStatus == "known" && (operation.InputTokens is null || operation.OutputTokens is null
            || request.InputTokens.HasValue && (request.InputTokens != operation.InputTokens || request.OutputTokens != operation.OutputTokens
                || request.CachedInputTokens != operation.CachedInputTokens))) return Conflict<bool>();
        var finalActor = await authorization.AuthorizeAsync(AiAgentRunPermissions.Reconcile, token).ConfigureAwait(false);
        if (finalActor != actor || !Current(finalActor) || Scope() != scope) return Forbidden<bool>();

        bool changed;
        try
        {
            changed = await transaction.ExecuteAsync(async work =>
            {
                Ensure(actor, scope); work.ThrowIfCancellationRequested();
                var claimed = await commands.ExecuteAsync(AiAgentRunSql.ReconcileOwned, AiSqlParameters.Create(
                    ("RunId", run.Id), ("ScopeKey", scope), ("ActorUserId", actor.UserId),
                    ("Version", request.ExpectedVersion), ("Now", clock.UtcNow)), work).ConfigureAwait(false);
                work.ThrowIfCancellationRequested(); Ensure(actor, scope);
                if (claimed == 0) return false;
                if (claimed != 1) throw new InvalidOperationException("Reconciliation claim did not affect one run.");
                // 采用原月份和原价格快照；不完整计量继续 unknown，不能按零用量释放。
                var usage = operation.UsageStatus == "known" ? new AiOperationUsage(operation.InputTokens, operation.OutputTokens, operation.CachedInputTokens)
                    : new(request.InputTokens, request.OutputTokens, request.CachedInputTokens);
                await budget.SettleAsync(runId, usage, operation.UsageStatus == "known" ? operation.Outcome : "failed", work).ConfigureAwait(false);
                work.ThrowIfCancellationRequested(); Ensure(actor, scope);
                var sequence = checked(await runs.FindLatestEventSequenceAsync(runId, work).ConfigureAwait(false) + 1);
                foreach (var item in new[] { (Type: "run.reconciled", Payload: payload), (Type: "run.failed", Payload: "{}") })
                {
                    work.ThrowIfCancellationRequested(); Ensure(actor, scope);
                    var inserted = await commands.ExecuteAsync(AiAgentRunSql.InsertEvent, AiSqlParameters.Create(
                        ("EventId", ids.NewId()), ("RunId", runId), ("Sequence", sequence++), ("EventType", item.Type),
                        ("PayloadVersion", 1), ("Payload", item.Payload), ("Now", clock.UtcNow)), work).ConfigureAwait(false);
                    if (inserted != 1) throw new InvalidOperationException("Reconciliation event was not persisted.");
                }
                work.ThrowIfCancellationRequested(); Ensure(actor, scope);
                // 数据库锁等待后再经权威契约复核；失效必须回滚运行、账本及证据，不能只返回失败。
                var commitActor = await independentAuthorization.AuthorizeAsync(AiAgentRunPermissions.Reconcile, work).ConfigureAwait(false);
                work.ThrowIfCancellationRequested(); Ensure(actor, scope);
                if (commitActor != actor) throw new ReconciliationAuthorizationExpiredException();
                return true;
            }, token).ConfigureAwait(false);
        }
        catch (ReconciliationAuthorizationExpiredException) { return Forbidden<bool>(); }
        catch (AiBudgetException error) when (error.Code == "ai.budget.settlement_conflict") { return Conflict<bool>(); }
        return changed ? Result<bool>.Success(true) : Conflict<bool>();
    }

    private async Task<AiOperationBudgetRecord?> OperationAsync(AiAgentRunRecord run, CancellationToken token)
    {
        var operation = await queries.QuerySingleOrDefaultAsync<AiOperationBudgetRecord>(AiOperationBudgetSql.Find,
            AiSqlParameters.Create(("OperationId", run.Id), ("ScopeKey", run.ScopeKey)), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (operation is null || operation.Id != run.Id || operation.RunId != run.Id) return null;
        AgentRunBudgetSnapshot? snapshot;
        try { snapshot = JsonSerializer.Deserialize(run.BudgetJson, AiJsonSerializerContext.Default.AgentRunBudgetSnapshot); }
        catch (JsonException) { return null; }
        return snapshot is not null && snapshot.ModelConfigId == operation.ModelConfigId
            && snapshot.InputTokenLimit is > 0 and <= AiExecutionBudget.MaximumTokens
            && snapshot.OutputTokenLimit is > 0 and <= AiExecutionBudget.MaximumTokens
            && operation.ReservedTokens == snapshot.InputTokenLimit + snapshot.OutputTokenLimit ? operation : null;
    }

    internal static bool Valid(ReconcileAiAgentRunRequest request) => request is not null && request.ClientRequestId != Guid.Empty
        && request.ExpectedVersion is > 0 and < long.MaxValue && request.EvidenceDigest is { Length: 64 }
        && request.EvidenceDigest.All(c => char.IsAsciiHexDigit(c) && !char.IsAsciiLetterLower(c))
        && (request.InputTokens is null && request.OutputTokens is null && request.CachedInputTokens is null
            || request.InputTokens is >= 0 and <= AiExecutionBudget.MaximumTokens
            && request.OutputTokens is >= 0 and <= AiExecutionBudget.MaximumTokens
            && (request.CachedInputTokens is null || request.CachedInputTokens >= 0 && request.CachedInputTokens <= request.InputTokens));

    private bool Current([NotNullWhen(true)] AuthorizedSessionActor? actor) => actor is not null && actor.UserId != Guid.Empty && actor.SessionId != Guid.Empty
        && actor.TenantId != Guid.Empty && actor.TenantId == tenant.Id && (tenant.Id.HasValue || tenant.IsHost);
    private static bool Owned([NotNullWhen(true)] AiAgentRunRecord? run, Guid runId, AuthorizedSessionActor actor, string scope) => run is not null
        && run.Id == runId && run.ActorUserId == actor.UserId && run.ScopeKey == scope && run.TenantId == actor.TenantId;
    private string Scope() => tenant.Id is { } id ? id.ToString("N") : tenant.IsHost ? "host" : throw new InvalidOperationException("AI reconciliation requires a trusted scope.");
    private void Ensure(AuthorizedSessionActor actor, string scope)
    { if (!Current(actor) || Scope() != scope) throw new ReconciliationAuthorizationExpiredException(); }
    private void RequireNoTransaction()
    { if (transactionState.HasTransaction) throw new InvalidOperationException("AI reconciliation cannot join an existing transaction."); }
    private static Result<T> Forbidden<T>() => Result<T>.Failure(new Error("authorization.permission_denied", "Reconciliation requires an authorized current session.", ErrorType.Forbidden));
    private static Result<T> NotFound<T>() => Result<T>.Failure(new Error(AiErrorCodes.AgentRunNotFound, "The agent run was not found.", ErrorType.NotFound));
    private static Result<T> Conflict<T>() => Result<T>.Failure(new Error("ai.agent_reconciliation.conflict", "The reconciliation conflicts with current or recorded state.", ErrorType.Conflict));
    private sealed class ReconciliationAuthorizationExpiredException : Exception { }
}
