using System.Text.Json;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Agents.Runtime;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Serialization;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>只修复历史终态的待结算预留；无需恢复用户权限，不交付正文或重新执行外部操作。</summary>
internal sealed class AiAgentTerminalBudgetRepair(AiAgentRunStore store, IQueryExecutor queries,
    ICommandTransaction transaction, IAiOperationBudgetStore budget, ICurrentTenant tenant)
{
    /// <summary>调用方必须先按持久运行绑定可信作用域；版本占有与账本结算共享本地事务。</summary>
    internal async Task<bool> RepairAsync(Guid runId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var run = await store.FindByIdAsync(runId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (run is null || run.Id != runId || !MatchesScope(run) || Outcome(run.StatusKey) is not { } outcome) return false;
        return await transaction.ExecuteAsync(async token =>
        {
            EnsureScope(run);
            var operation = await queries.QuerySingleOrDefaultAsync<AiOperationBudgetRecord>(AiOperationBudgetSql.Find,
                AiSqlParameters.Create(("OperationId", runId), ("ScopeKey", run.ScopeKey)), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (operation is null || operation.Id != runId || operation.RunId != runId || operation.ModelConfigId == Guid.Empty
                || operation.UsageStatus != "reserved" || operation.Outcome != "pending") return false;
            EnsureScope(run);
            var claimed = await store.ClaimTerminalBudgetRepairAsync(run, operation, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (claimed == 0) return false;
            if (claimed != 1) throw new InvalidOperationException("Terminal budget repair claim did not affect one run.");
            var checkpoint = await store.FindLatestCheckpointAsync(runId, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var usage = OriginalUsage(run, operation, checkpoint);
            EnsureScope(run);
            await budget.SettleAsync(runId, usage, outcome, token).ConfigureAwait(false);
            // 下层即使忽略取消或作用域变化，也不能让外层继续物理提交。
            token.ThrowIfCancellationRequested();
            EnsureScope(run);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    private bool MatchesScope(AiAgentRunRecord run) => run.TenantId is { } id
        ? id != Guid.Empty && tenant.Id == id && run.ScopeKey == id.ToString("N")
        : tenant.IsHost && tenant.Id is null && run.ScopeKey == "host";

    private void EnsureScope(AiAgentRunRecord run)
    {
        if (!MatchesScope(run)) throw new InvalidOperationException("Terminal budget repair scope changed.");
    }

    internal static string? Outcome(string status) => status switch
    {
        "completed" => "succeeded",
        "failed" => "failed",
        "cancelled" or "authorization_required" or "expired" => "cancelled",
        _ => null
    };

    internal static AiOperationUsage OriginalUsage(AiAgentRunRecord run, AiOperationBudgetRecord operation, AgentCheckpointRecord? checkpoint)
    {
        // 老版本载荷或缺失计量只收敛为 unknown，不凭终态、步骤计数或空结果退款。
        if (checkpoint is null || string.IsNullOrWhiteSpace(run.BudgetJson) || run.BudgetJson.Length > 262_144) return new(null, null);
        AgentRunBudgetSnapshot? snapshot;
        try { snapshot = JsonSerializer.Deserialize(run.BudgetJson, AiJsonSerializerContext.Default.AgentRunBudgetSnapshot); }
        catch (JsonException) { return new(null, null); }
        if (snapshot is null || snapshot.PromptBinding is not null || snapshot.Prompt is null || snapshot.ModelConfigId != operation.ModelConfigId
            || snapshot.InputTokenLimit is <= 0 or > AiExecutionBudget.MaximumTokens
            || snapshot.OutputTokenLimit is <= 0 or > AiExecutionBudget.MaximumTokens
            || operation.ReservedTokens != checked(snapshot.InputTokenLimit + snapshot.OutputTokenLimit)) return new(null, null);
        var state = AiModelRunCheckpoint.Restore(run, snapshot, checkpoint);
        if (state is null || state.InputDigest != AiModelRunCheckpoint.Digest(snapshot.Prompt)) return new(null, null);
        // 仅单模型未派发且失败的兼容凭证可证明零用量；矛盾终态与整轮工具循环保留未知。
        if (state.Phase == "not_dispatched") return run.StatusKey == "failed" ? new(0, 0) : new(null, null);
        return state.Phase == "completed" && state.Result is { InputTokens: not null, OutputTokens: not null } result
            ? new(result.InputTokens, result.OutputTokens) : new(null, null);
    }
}
