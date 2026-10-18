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
        // 查询可能异步等待；创建来源只在入口读取一次，幂等重试不得覆盖已存上下文。
        var traceParent = AiAgentRunTraceContext.Capture();
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
                    ("SessionKind", draft.SessionKind),
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
                    ("TraceParent", traceParent),
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

    /// <summary>事件序号独立于检查点；工具循环等待事件没有检查点也必须保持单调递增。</summary>
    internal async ValueTask<long> FindLatestEventSequenceAsync(Guid runId, CancellationToken cancellationToken) =>
        await queries.QuerySingleOrDefaultAsync<long>(AiAgentRunSql.FindLatestEventSequence,
            AiSqlParameters.Create(("RunId", runId)), cancellationToken).ConfigureAwait(false);

    public async ValueTask<bool> CommitProgressAsync(AgentRunProgressCommit commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (!AgentRunState.CanTransitionToCommitted(commit.NewStatusKey))
        {
            throw new InvalidOperationException("Invalid agent run terminal or progress status.");
        }

        AgentExecutionTelemetry.Observation? observation = null;
        try
        {
            var written = await transaction.ExecuteAsync(WriteAsync, cancellationToken).ConfigureAwait(false);
            // completed 只表示此方法返回；嵌套事务的外层仍可能回滚，观测不是提交凭证。
            if (written && observation is not null) observation.Outcome = "completed";
            return written;
        }
        finally { observation?.Dispose(); }

        async Task<bool> WriteAsync(CancellationToken token)
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
                // 租约 fencing 成功后才关联真实步骤；沿用调用方 Trace，无采样时允许保留空值。
                observation = AgentExecutionTelemetry.StartStep(commit.Lease.RunId, step.StepId, step.OperationId, cancellationToken);
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
                        ("TraceId", Full.NET.AI.Abstractions.Models.AiDataObservationScope.CanObserve ? step.TraceId ?? observation.TraceId : null),
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
        }
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
            AiSqlParameters.Create(("BatchSize", batchSize), ("Now", now),
                ("WorkflowKey", Full.NET.Agents.Workflows.AgentWorkflowRegistry.ChatRenameWorkflowKey),
                ("SingleModelKey", Full.NET.Agents.Definitions.AgentDefinitionRegistry.SingleTextKey),
                ("ToolLoopKey", Full.NET.Agents.Definitions.AgentDefinitionRegistry.ReadOnlyToolLoopKey)),
            cancellationToken).ConfigureAwait(false);
        return rows.ToList();
    }

    /// <summary>跨作用域只扫描已有终态和匹配的待结算预留；实际修复重新绑定可信租户。</summary>
    internal async Task<IReadOnlyList<Guid>> ScanTerminalUnsettledAsync(int batchSize, CancellationToken cancellationToken, Guid? afterRunId = null,
        bool includeCompletedReceipts = false)
    {
        if (batchSize <= 0) return [];
        var statement = database.Value.Provider == DatabaseProvider.MySql
            ? AiAgentRunSql.ScanTerminalBudgetMySql : AiAgentRunSql.ScanTerminalBudgetSqlServer;
        var rows = await queries.QueryAsync<Guid>(statement, AiSqlParameters.Create(("BatchSize", Math.Min(batchSize, 100)), ("AfterRunId", afterRunId),
            ("IncludeCompletedReceipts", includeCompletedReceipts ? 1 : 0)), cancellationToken)
            .ConfigureAwait(false);
        return rows.ToList();
    }

    /// <summary>须在账本修复的外层事务内调用；零行表示状态、版本或预留已变化，不得继续结算。</summary>
    internal Task<int> ClaimTerminalBudgetRepairAsync(AiAgentRunRecord run, AiOperationBudgetRecord operation, CancellationToken token) =>
        commands.ExecuteAsync(AiAgentRunSql.ClaimTerminalBudgetRepair, AiSqlParameters.Create(
            ("RunId", run.Id), ("ScopeKey", run.ScopeKey), ("Version", run.Version), ("StatusKey", run.StatusKey),
            ("ModelConfigId", operation.ModelConfigId), ("RequestHash", operation.RequestHash), ("Now", clock.UtcNow)), token);

    public async ValueTask<bool> TryCancelOwnedAsync(
        Guid runId, string scopeKey, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await transaction.ExecuteAsync(async token =>
        {
            var affected = await commands.ExecuteAsync(
                AiAgentRunSql.CancelOwned,
                AiSqlParameters.Create(("RunId", runId), ("ScopeKey", scopeKey), ("ActorUserId", actorUserId), ("Now", clock.UtcNow)),
                token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (affected != 1) return false;

            // 条件更新持有运行行锁，先使旧租约失效，再分配序号，避免取消与 Worker 完成事件交错提交。
            var sequence = checked(await FindLatestEventSequenceAsync(runId, token).ConfigureAwait(false) + 1);
            token.ThrowIfCancellationRequested();
            var inserted = await commands.ExecuteAsync(AiAgentRunSql.InsertEvent, AiSqlParameters.Create(
                ("EventId", ids.NewId()), ("RunId", runId), ("Sequence", sequence),
                ("EventType", "run.cancelled"), ("PayloadVersion", 1), ("Payload", "{}"), ("Now", clock.UtcNow)), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (inserted != 1) throw new InvalidOperationException("The run cancellation event was not persisted.");
            return true;
        }, cancellationToken).ConfigureAwait(false);
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
