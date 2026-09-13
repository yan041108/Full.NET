using Full.NET.Agents.AgUi;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.AgUi;

/// <summary>fn_ai_agent_* 的 AG-UI 只读适配；重放事件不触发 Worker 或工具执行。</summary>
internal sealed class AiAgentRunAgUiReader(
    AiAgentRunStore runs,
    IQueryExecutor queries,
    IOptions<DatabaseOptions> database) : IAgentRunAgUiReader
{
    public async ValueTask<AgentRunAgUiSnapshot?> TryGetOwnedSnapshotAsync(
        Guid runId,
        string scopeKey,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var record = await runs.FindOwnedAsync(runId, scopeKey, actorUserId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        var bounds = await queries.QuerySingleOrDefaultAsync<AiAgentEventSequenceBounds>(
            AiAgentEventSql.MaxSequence,
            AiSqlParameters.Create(("RunId", runId)),
            cancellationToken).ConfigureAwait(false);
        return new(
            record.Id,
            record.SessionId,
            record.StatusKey,
            record.DefinitionKey,
            bounds?.MaxSequence ?? 0);
    }

    public async ValueTask<IReadOnlyList<AgentRunPersistedEvent>> ListEventsAfterAsync(
        Guid runId,
        long afterSequence,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return [];
        }

        var statement = database.Value.Provider == DatabaseProvider.MySql
            ? AiAgentEventSql.ListAfterSequenceMySql
            : AiAgentEventSql.ListAfterSequenceSqlServer;
        var rows = await queries.QueryAsync<AiAgentEventRecord>(
            statement,
            AiSqlParameters.Create(("RunId", runId), ("AfterSequence", afterSequence), ("Limit", limit)),
            cancellationToken).ConfigureAwait(false);
        return rows.Select(row => new AgentRunPersistedEvent(
            row.Sequence,
            row.EventType,
            row.PayloadVersion,
            row.Payload,
            row.CreatedAtUtc)).ToList();
    }

    public async ValueTask<AgentRunAgUiProgress?> TryGetProgressAsync(
        Guid runId,
        string scopeKey,
        CancellationToken cancellationToken = default)
    {
        var budgetStatement = database.Value.Provider == DatabaseProvider.MySql
            ? AiAgentEventSql.FindBudgetByRunMySql
            : AiAgentEventSql.FindBudgetByRunSqlServer;
        var budget = await queries.QuerySingleOrDefaultAsync<AiAgentRunBudgetSummaryRecord>(
            budgetStatement,
            AiSqlParameters.Create(("RunId", runId), ("ScopeKey", scopeKey)),
            cancellationToken).ConfigureAwait(false);
        var steps = await queries.QueryAsync<AiAgentStepSummaryRecord>(
            AiAgentEventSql.ListSteps,
            AiSqlParameters.Create(("RunId", runId)),
            cancellationToken).ConfigureAwait(false);
        if (budget is null && steps.Count == 0)
        {
            return null;
        }

        return new(
            budget?.InputTokens,
            budget?.OutputTokens,
            budget?.UsageStatus,
            budget?.Outcome,
            steps.Select(step => new AgentRunAgUiStepSummary(
                step.StepKey,
                step.Attempt,
                step.StatusKey,
                step.InputTokens,
                step.OutputTokens,
                step.ErrorCode)).ToList());
    }
}
