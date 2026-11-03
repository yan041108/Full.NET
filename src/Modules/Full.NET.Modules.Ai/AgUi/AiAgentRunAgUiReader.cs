using Full.NET.Agents.AgUi;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Microsoft.Extensions.Options;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Ai.AgUi;

/// <summary>fn_ai_agent_* 的 AG-UI 只读适配；重放事件不触发 Worker 或工具执行。</summary>
internal sealed class AiAgentRunAgUiReader(
    AiAgentRunStore runs,
    IQueryExecutor queries,
    IOptions<DatabaseOptions> database,
    ICurrentTenant tenant,
    ICurrentSessionAuthorization authorization,
    ILogger<AiAgentRunAgUiReader> logger) : IAgentRunAgUiReader
{
    private AuthorizedSessionActor? original;

    public async ValueTask<bool> CanReadAsync(Guid runId, string scopeKey, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var expectedScope = tenant.Id is { } id ? id.ToString("N") : tenant.IsHost ? "host" : null;
        if (runId == Guid.Empty || actorUserId == Guid.Empty || expectedScope is null || expectedScope != scopeKey) return false;
        try
        {
            // Identity权威Port统一核对原JWT有效期、会话、强制改密、租户和当前权限，不在Ai重复解析凭据。
            var actor = await authorization.AuthorizeAsync(AiAgentRunPermissions.Read, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var currentScope = tenant.Id is { } currentId ? currentId.ToString("N") : tenant.IsHost ? "host" : null;
            if (actor is null || actor.UserId != actorUserId || actor.SessionId == Guid.Empty || actor.TenantId != tenant.Id || currentScope != scopeKey) return false;
            original ??= actor;
            return actor == original;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // 会话核证不可用时关闭读取；诊断不携带令牌、安全戳或原始异常正文。
            try { logger.LogWarning("Agent event admission unavailable for run {RunId}.", runId); } catch (Exception) { }
            return false;
        }
    }

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
