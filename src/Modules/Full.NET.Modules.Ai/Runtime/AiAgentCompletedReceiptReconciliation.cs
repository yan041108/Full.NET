using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>仅用原已完成凭证关闭未知运行并结算原账本；不读取外部模型、不重发、不交付历史正文。</summary>
internal sealed class AiAgentCompletedReceiptReconciliation(AiAgentRunStore store, IQueryExecutor queries, ICommandExecutor commands,
    ICommandTransaction transaction, IAiOperationBudgetStore budget, ICurrentTenant tenant, IClock clock, IIdGenerator ids,
    IDataTransactionState transactions)
{
    internal async Task<bool> ReconcileAsync(Guid runId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (transactions.HasTransaction) throw new InvalidOperationException("Automatic receipt reconciliation cannot join an ambient transaction.");
        var run = await store.FindByIdAsync(runId, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (run is null || run.Id != runId || run.ActorUserId == Guid.Empty || run.SessionId == Guid.Empty
            || run.StatusKey != "reconciliation_required" || !SameScope(run)) return false;
        return await transaction.ExecuteAsync(async work =>
        {
            Check(run, work);
            var operation = await queries.QuerySingleOrDefaultAsync<AiOperationBudgetRecord>(AiOperationBudgetSql.Find,
                AiSqlParameters.Create(("OperationId", runId), ("ScopeKey", run.ScopeKey)), work).ConfigureAwait(false);
            Check(run, work);
            if (operation is null || operation.Id != runId || operation.RunId != runId || operation.ModelConfigId == Guid.Empty
                || operation.UsageStatus is not ("reserved" or "unknown" or "known")) return false;
            var checkpoint = await store.FindLatestCheckpointAsync(runId, work).ConfigureAwait(false); Check(run, work);
            var usage = AiAgentTerminalBudgetRepair.OriginalUsage(run, operation, checkpoint);
            // started、缺失、歧义或不支持的旧凭证继续待人工核证，不能把它们换成零或新的派发。
            if (usage.InputTokens is null || usage.OutputTokens is null || checkpoint is null) return false;
            if (operation.UsageStatus == "known")
            {
                if (operation.InputTokens != usage.InputTokens || operation.OutputTokens != usage.OutputTokens
                    || operation.Outcome is not ("succeeded" or "failed" or "cancelled")) return false;
                usage = new(operation.InputTokens, operation.OutputTokens, operation.CachedInputTokens);
            }
            AiExecutionBudget.ValidateUsage(usage);
            var claimed = await commands.ExecuteAsync(AiAgentRunSql.ReconcileOwned, AiSqlParameters.Create(
                ("RunId", runId), ("ScopeKey", run.ScopeKey), ("ActorUserId", run.ActorUserId),
                ("Version", run.Version), ("Now", clock.UtcNow)), work).ConfigureAwait(false);
            Check(run, work);
            if (claimed == 0) return false;
            if (claimed != 1) throw new InvalidOperationException("Automatic receipt reconciliation did not claim exactly one run.");
            // 运行行锁围住最后凭证复核与结算；若读取后发生竞争，整笔运行状态和账本必须回滚。
            var finalCheckpoint = await store.FindLatestCheckpointAsync(runId, work).ConfigureAwait(false); Check(run, work);
            if (finalCheckpoint is null || finalCheckpoint.CheckpointId != checkpoint.CheckpointId || finalCheckpoint.Sequence != checkpoint.Sequence
                || finalCheckpoint.Checksum != checkpoint.Checksum || finalCheckpoint.PayloadProtected != checkpoint.PayloadProtected)
                throw new InvalidOperationException("The completed receipt changed while reconciling.");
            await budget.SettleAsync(runId, usage, operation.UsageStatus == "known" ? operation.Outcome : "failed", work).ConfigureAwait(false);
            Check(run, work);
            var sequence = checked(await store.FindLatestEventSequenceAsync(runId, work).ConfigureAwait(false) + 1); Check(run, work);
            foreach (var item in new[] { (Type: "run.automatically_reconciled", Payload: """{"evidence":"completed_checkpoint","replayed":false}"""),
                (Type: "run.failed", Payload: "{}") })
            {
                var added = await commands.ExecuteAsync(AiAgentRunSql.InsertEvent, AiSqlParameters.Create(("EventId", ids.NewId()),
                    ("RunId", runId), ("Sequence", sequence++), ("EventType", item.Type), ("PayloadVersion", 1),
                    ("Payload", item.Payload), ("Now", clock.UtcNow)), work).ConfigureAwait(false);
                Check(run, work); if (added != 1) throw new InvalidOperationException("Automatic reconciliation audit was not persisted.");
            }
            return true;
        }, token).ConfigureAwait(false);
    }

    private bool SameScope(AiAgentRunRecord run) => run.TenantId is { } id
        ? id != Guid.Empty && tenant.Id == id && !tenant.IsHost && run.ScopeKey == id.ToString("N")
        : tenant.Id is null && tenant.IsHost && run.ScopeKey == "host";
    private void Check(AiAgentRunRecord run, CancellationToken token)
    { token.ThrowIfCancellationRequested(); if (!SameScope(run)) throw new InvalidOperationException("Automatic reconciliation scope changed."); }
}
