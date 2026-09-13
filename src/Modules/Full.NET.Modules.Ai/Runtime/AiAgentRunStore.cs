using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Agents.Runtime;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>fn_ai_agent_* 唯一所有者；租约 fencing 与版本条件写入防止旧 Worker 提交。</summary>
internal sealed class AiAgentRunStore(
    IQueryExecutor queries,
    ICommandExecutor commands,
    ICommandTransaction transaction,
    IIdGenerator ids,
    IClock clock,
    IOptions<DatabaseOptions> database) : IAgentRunStore
{
    public async ValueTask<Guid> CreateOrGetAsync(AgentRunDraft draft, CancellationToken cancellationToken = default)
    {
        ValidateDraft(draft);
        var existing = await queries.QuerySingleOrDefaultAsync<Guid>(
            AiAgentRunSql.FindByClientRequest,
            AiSqlParameters.Create(
                ("ScopeKey", draft.ScopeKey),
                ("ActorUserId", draft.ActorUserId),
                ("ClientRequestId", draft.ClientRequestId)),
            cancellationToken).ConfigureAwait(false);
        if (existing != Guid.Empty)
        {
            return existing;
        }

        var id = draft.PredeterminedRunId ?? ids.NewId();
        var now = clock.UtcNow;
        try
        {
            var affected = await commands.ExecuteAsync(
                AiAgentRunSql.InsertRun,
                AiSqlParameters.Create(
                    ("Id", id),
                    ("ScopeKey", draft.ScopeKey),
                    ("TenantId", draft.TenantId),
                    ("ActorUserId", draft.ActorUserId),
                    ("SessionId", draft.SessionId),
                    ("ClientRequestId", draft.ClientRequestId),
                    ("RequestHash", draft.RequestHash),
                    ("DefinitionKey", draft.DefinitionKey),
                    ("DefinitionVersion", draft.DefinitionVersion),
                    ("AuthorizationBindingId", draft.AuthorizationBindingId),
                    ("SecurityStamp", draft.SecurityStamp),
                    ("ActorScope", draft.ActorScope),
                    ("EffectiveScope", draft.EffectiveScope),
                    ("BudgetJson", draft.BudgetJson),
                    ("DeadlineAtUtc", draft.DeadlineAtUtc),
                    ("Now", now)),
                cancellationToken).ConfigureAwait(false);
            if (affected != 1)
            {
                throw new InvalidOperationException("Agent run create affected zero rows.");
            }

            return id;
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            var raced = await queries.QuerySingleOrDefaultAsync<Guid>(
                AiAgentRunSql.FindByClientRequest,
                AiSqlParameters.Create(
                    ("ScopeKey", draft.ScopeKey),
                    ("ActorUserId", draft.ActorUserId),
                    ("ClientRequestId", draft.ClientRequestId)),
                cancellationToken).ConfigureAwait(false);
            return raced == Guid.Empty ? throw new InvalidOperationException("Agent run idempotency race lost.", ex) : raced;
        }
    }

    public async ValueTask<AgentRunLease?> TryAcquireAsync(
        Guid runId,
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        if (runId == Guid.Empty || string.IsNullOrWhiteSpace(workerId) || workerId.Length > 128 || leaseDuration <= TimeSpan.Zero)
        {
            return null;
        }

        var expires = now.Add(leaseDuration);
        var acquire = database.Value.Provider == DatabaseProvider.MySql
            ? AiAgentRunSql.AcquireLeaseMySql
            : AiAgentRunSql.AcquireLeaseSqlServer;
        var affected = await commands.ExecuteAsync(
            acquire,
            AiSqlParameters.Create(("RunId", runId), ("WorkerId", workerId), ("LeaseExpiresAtUtc", expires), ("Now", now)),
            cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            return null;
        }

        var row = await queries.QuerySingleOrDefaultAsync<AiAgentRunRecord>(
            AiAgentRunSql.SelectLease,
            AiSqlParameters.Create(("RunId", runId), ("WorkerId", workerId)),
            cancellationToken).ConfigureAwait(false);
        return row is null ? null : new(runId, workerId, row.LeaseEpoch, row.Version, row.LeaseExpiresAtUtc ?? expires);
    }

    public async ValueTask<bool> RenewAsync(
        AgentRunLease lease,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        if (lease.RunId == Guid.Empty || string.IsNullOrWhiteSpace(lease.WorkerId) || leaseDuration <= TimeSpan.Zero)
        {
            return false;
        }

        var affected = await commands.ExecuteAsync(
            AiAgentRunSql.RenewLease,
            AiSqlParameters.Create(
                ("RunId", lease.RunId),
                ("WorkerId", lease.WorkerId),
                ("LeaseEpoch", lease.Epoch),
                ("Version", lease.Version),
                ("LeaseExpiresAtUtc", now.Add(leaseDuration)),
                ("Now", now)),
            cancellationToken).ConfigureAwait(false);
        return affected == 1;
    }

    public async ValueTask<AgentCheckpointRecord?> FindLatestCheckpointAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        if (runId == Guid.Empty)
        {
            return null;
        }

        var statement = database.Value.Provider == DatabaseProvider.MySql
            ? AiAgentRunSql.FindLatestCheckpointMySql
            : AiAgentRunSql.FindLatestCheckpoint;
        return await queries.QuerySingleOrDefaultAsync<AgentCheckpointRecord>(
            statement,
            AiSqlParameters.Create(("RunId", runId)),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> CommitProgressAsync(AgentRunProgressCommit commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (!AgentRunState.CanTransitionToCommitted(commit.NewStatusKey))
        {
            throw new InvalidOperationException("Invalid agent run terminal or progress status.");
        }

        return await transaction.ExecuteAsync(async token =>
        {
            var affected = await commands.ExecuteAsync(
                AiAgentRunSql.UpdateRunStatus,
                AiSqlParameters.Create(
                    ("RunId", commit.Lease.RunId),
                    ("WorkerId", commit.Lease.WorkerId),
                    ("LeaseEpoch", commit.Lease.Epoch),
                    ("Version", commit.Lease.Version),
                    ("StatusKey", commit.NewStatusKey),
                    ("Now", clock.UtcNow)),
                token).ConfigureAwait(false);
            if (affected != 1)
            {
                return false;
            }

            if (commit.Step is { } step)
            {
                await commands.ExecuteAsync(
                    AiAgentRunSql.InsertStep,
                    AiSqlParameters.Create(
                        ("StepId", step.StepId),
                        ("RunId", commit.Lease.RunId),
                        ("StepKey", step.StepKey),
                        ("Attempt", step.Attempt),
                        ("OperationId", step.OperationId),
                        ("StatusKey", step.StatusKey),
                        ("ModelConfigVersion", step.ModelConfigVersion),
                        ("ToolVersion", step.ToolVersion),
                        ("PriceVersionId", step.PriceVersionId),
                        ("InputTokens", step.InputTokens),
                        ("OutputTokens", step.OutputTokens),
                        ("Cost", step.Cost),
                        ("Currency", step.Currency),
                        ("TraceId", step.TraceId),
                        ("ArgumentDigest", step.ArgumentDigest),
                        ("ErrorCode", step.ErrorCode),
                        ("Now", clock.UtcNow)),
                    token).ConfigureAwait(false);
            }

            if (commit.Checkpoint is { } checkpoint)
            {
                await commands.ExecuteAsync(
                    AiAgentRunSql.InsertCheckpoint,
                    AiSqlParameters.Create(
                        ("CheckpointId", checkpoint.CheckpointId),
                        ("RunId", commit.Lease.RunId),
                        ("Sequence", checkpoint.Sequence),
                        ("FormatVersion", checkpoint.FormatVersion),
                        ("FrameworkVersion", checkpoint.FrameworkVersion),
                        ("DefinitionVersion", checkpoint.DefinitionVersion),
                        ("PayloadProtected", checkpoint.PayloadProtected),
                        ("Checksum", checkpoint.Checksum),
                        ("Now", clock.UtcNow)),
                    token).ConfigureAwait(false);
            }

            if (commit.Event is { } eventRow)
            {
                await commands.ExecuteAsync(
                    AiAgentRunSql.InsertEvent,
                    AiSqlParameters.Create(
                        ("EventId", eventRow.EventId),
                        ("RunId", commit.Lease.RunId),
                        ("Sequence", eventRow.Sequence),
                        ("EventType", eventRow.EventType),
                        ("PayloadVersion", eventRow.PayloadVersion),
                        ("Payload", eventRow.Payload),
                        ("Now", clock.UtcNow)),
                    token).ConfigureAwait(false);
            }

            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateDraft(AgentRunDraft draft)
    {
        if (draft.ClientRequestId == Guid.Empty || draft.ActorUserId == Guid.Empty || draft.SessionId == Guid.Empty
            || draft.AuthorizationBindingId == Guid.Empty || string.IsNullOrWhiteSpace(draft.ScopeKey) || draft.ScopeKey.Length > 32
            || string.IsNullOrWhiteSpace(draft.RequestHash) || draft.RequestHash.Length != 64
            || string.IsNullOrWhiteSpace(draft.DefinitionKey) || draft.DefinitionKey.Length > 64 || draft.DefinitionVersion < 1
            || string.IsNullOrWhiteSpace(draft.SecurityStamp) || string.IsNullOrWhiteSpace(draft.ActorScope)
            || string.IsNullOrWhiteSpace(draft.EffectiveScope) || string.IsNullOrWhiteSpace(draft.BudgetJson)
            || draft.DeadlineAtUtc <= DateTimeOffset.MinValue)
        {
            throw new ArgumentException("Invalid agent run draft.", nameof(draft));
        }
    }

    public async ValueTask<Guid> FindClientRequestAsync(
        string scopeKey, Guid actorUserId, Guid clientRequestId, CancellationToken cancellationToken = default) =>
        await queries.QuerySingleOrDefaultAsync<Guid>(
            AiAgentRunSql.FindByClientRequest,
            AiSqlParameters.Create(("ScopeKey", scopeKey), ("ActorUserId", actorUserId), ("ClientRequestId", clientRequestId)),
            cancellationToken).ConfigureAwait(false);

    public async ValueTask<AiAgentRunRecord?> FindOwnedAsync(
        Guid runId, string scopeKey, Guid actorUserId, CancellationToken cancellationToken = default) =>
        await queries.QuerySingleOrDefaultAsync<AiAgentRunRecord>(
            AiAgentRunSql.FindOwned,
            AiSqlParameters.Create(("RunId", runId), ("ScopeKey", scopeKey), ("ActorUserId", actorUserId)),
            cancellationToken).ConfigureAwait(false);

    public async ValueTask<AiAgentRunRecord?> FindByIdAsync(Guid runId, CancellationToken cancellationToken = default) =>
        await queries.QuerySingleOrDefaultAsync<AiAgentRunRecord>(
            AiAgentRunSql.FindById,
            AiSqlParameters.Create(("RunId", runId)),
            cancellationToken).ConfigureAwait(false);

    public async ValueTask<IReadOnlyList<Guid>> ScanQueuedAsync(int batchSize, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
        {
            return [];
        }

        var statement = database.Value.Provider == DatabaseProvider.MySql
            ? AiAgentRunSql.ScanQueuedMySql
            : AiAgentRunSql.ScanQueuedSqlServer;
        var rows = await queries.QueryAsync<Guid>(
            statement,
            AiSqlParameters.Create(("BatchSize", batchSize), ("Now", now)),
            cancellationToken).ConfigureAwait(false);
        return rows.ToList();
    }

    public async ValueTask<bool> TryCancelOwnedAsync(
        Guid runId, string scopeKey, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var affected = await commands.ExecuteAsync(
            AiAgentRunSql.CancelOwned,
            AiSqlParameters.Create(("RunId", runId), ("ScopeKey", scopeKey), ("ActorUserId", actorUserId), ("Now", clock.UtcNow)),
            cancellationToken).ConfigureAwait(false);
        return affected == 1;
    }

    public async ValueTask<bool> TryResumeOwnedAsync(
        Guid runId,
        string scopeKey,
        Guid actorUserId,
        Guid sessionId,
        string securityStamp,
        CancellationToken cancellationToken = default)
    {
        var affected = await commands.ExecuteAsync(
            AiAgentRunSql.ResumeOwned,
            AiSqlParameters.Create(
                ("RunId", runId),
                ("ScopeKey", scopeKey),
                ("ActorUserId", actorUserId),
                ("SessionId", sessionId),
                ("SecurityStamp", securityStamp),
                ("Now", clock.UtcNow)),
            cancellationToken).ConfigureAwait(false);
        return affected == 1;
    }

    private static bool IsUniqueViolation(Exception ex) =>
        ex.Message.Contains("UX_fn_ai_agent_run_Client", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
}
