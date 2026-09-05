using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>扫描逾期待办，并在可信租户上下文内原子提交信号状态与可靠事件。</summary>
/// <param name="queryExecutor">执行有界全局扫描。</param>
/// <param name="commandExecutor">条件推进待办和写执行日志。</param>
/// <param name="transaction">Workflow 本地事务。</param>
/// <param name="clock">统一 UTC 时钟。</param>
/// <param name="idGenerator">生成执行日志标识。</param>
/// <param name="databaseOptions">选择双库等价扫描语句。</param>
/// <param name="tenantResolver">按数据库候选解析活动租户。</param>
/// <param name="currentTenant">仅供后台基础设施建立可信作用域。</param>
/// <param name="notificationPublisher">事务 Outbox 发布器。</param>
/// <param name="scanCursor">跨 DI 作用域保存的有界扫描游标。</param>
internal sealed class WorkflowTodoTimeoutProcessor(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions,
    IActiveTenantContextResolver tenantResolver,
    ICurrentTenantContextWriter currentTenant,
    WorkflowNotificationOutboxPublisher notificationPublisher,
    WorkflowTodoTimeoutScanCursor scanCursor)
{
    private const int BatchSize = 50;

    /// <summary>处理一批到期信号；返回扫描候选数供轮询节流。</summary>
    /// <param name="cancellationToken">取消当前批次的令牌。</param>
    /// <returns>本批扫描到的候选数量。</returns>
    public async Task<int> ProcessDueAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var cursor = scanCursor.Read();
        currentTenant.SetHost();
        IReadOnlyList<WorkflowTodoTimeoutCandidateRecord> candidates;
        try
        {
            var statement = databaseOptions.Value.Provider == DatabaseProvider.SqlServer
                ? WorkflowTodoTimeoutSql.ScanDueSqlServer
                : WorkflowTodoTimeoutSql.ScanDueMySql;
            candidates = await queryExecutor.QueryAsync<WorkflowTodoTimeoutCandidateRecord>(
                statement,
                WorkflowSqlParameters.Create(
                    ("Now", now), ("Take", BatchSize),
                    ("HasAfter", cursor.SignalAtUtc is null ? 0 : 1),
                    ("AfterSignalAtUtc", cursor.SignalAtUtc ?? now),
                    ("AfterTodoId", cursor.TodoId ?? Guid.Empty)),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            currentTenant.Clear();
        }

        scanCursor.Advance(candidates, BatchSize);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await TrySetScopeAsync(candidate, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            try
            {
                var processed = await transaction.ExecuteAsync(
                    token => ProcessOneAsync(candidate, now, token), cancellationToken)
                    .ConfigureAwait(false);
                _ = processed;
            }
            finally
            {
                currentTenant.Clear();
            }
        }

        return candidates.Count;
    }

    /// <summary>仅从可信数据库候选建立 Host 或活动租户上下文。</summary>
    /// <param name="candidate">超时扫描候选。</param>
    /// <param name="cancellationToken">取消租户解析的令牌。</param>
    /// <returns>作用域可安全建立时返回真。</returns>
    private async Task<bool> TrySetScopeAsync(
        WorkflowTodoTimeoutCandidateRecord candidate,
        CancellationToken cancellationToken)
    {
        if (candidate.ScopeKey == "host" && candidate.TenantId is null)
        {
            currentTenant.SetHost();
            return true;
        }

        if (candidate.ScopeKey != "tenant" || candidate.TenantId is not { } tenantId)
        {
            return false;
        }

        var tenant = await tenantResolver.ResolveActiveByIdAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
        if (tenant is null)
        {
            return false;
        }

        currentTenant.SetTenant(tenant);
        return true;
    }

    /// <summary>以升级优先规则提交单个信号；CAS 未命中表示被其他 Worker 或用户动作抢先处理。</summary>
    /// <param name="candidate">超时扫描候选。</param>
    /// <param name="now">本批统一 UTC 时间。</param>
    /// <param name="cancellationToken">取消事务的令牌。</param>
    /// <returns>成功提交并发布信号时返回真；无信号或 CAS 未命中时返回假。</returns>
    private async Task<bool> ProcessOneAsync(
        WorkflowTodoTimeoutCandidateRecord candidate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (candidate.EscalatedAtUtc is null &&
            candidate.EscalateAtUtc is { } escalateAt && escalateAt <= now &&
            candidate.EscalationRecipientUserId is { } escalationRecipient)
        {
            var updated = await commandExecutor.ExecuteAsync(
                WorkflowTodoTimeoutSql.CommitEscalation,
                WorkflowSqlParameters.Create(
                    ("TodoId", candidate.TodoId), ("Revision", candidate.Revision),
                    ("ExpectedSignalAtUtc", candidate.NextTimeoutSignalAtUtc),
                    ("TenantScopeKey", candidate.TenantScopeKey), ("Now", now)),
                cancellationToken).ConfigureAwait(false);
            if (updated != 1)
            {
                return false;
            }

            await WriteLogAsync(candidate, "todo.timeout.escalated", now, cancellationToken)
                .ConfigureAwait(false);
            await notificationPublisher.PublishTodoEscalationAsync(
                candidate.InstanceId, candidate.TodoId, escalationRecipient,
                candidate.BusinessType, candidate.BusinessId, now, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }

        if (candidate.NextReminderAtUtc is not { } reminderAt || reminderAt > now ||
            candidate.ReminderCount >= candidate.MaxReminderCount)
        {
            return false;
        }

        var nextCount = candidate.ReminderCount + 1;
        var nextReminder = nextCount < candidate.MaxReminderCount
            ? now.AddMinutes(candidate.ReminderIntervalMinutes)
            : (DateTimeOffset?)null;
        var nextSignal = Min(nextReminder,
            candidate.EscalatedAtUtc is null ? candidate.EscalateAtUtc : null);
        var reminderUpdated = await commandExecutor.ExecuteAsync(
            WorkflowTodoTimeoutSql.CommitReminder,
            WorkflowSqlParameters.Create(
                ("TodoId", candidate.TodoId), ("Revision", candidate.Revision),
                ("ExpectedSignalAtUtc", candidate.NextTimeoutSignalAtUtc),
                ("TenantScopeKey", candidate.TenantScopeKey), ("Now", now),
                ("ReminderCount", nextCount), ("NextReminderAtUtc", nextReminder),
                ("NextTimeoutSignalAtUtc", nextSignal)),
            cancellationToken).ConfigureAwait(false);
        if (reminderUpdated != 1)
        {
            return false;
        }

        await WriteLogAsync(candidate, $"todo.timeout.reminder.{nextCount}", now, cancellationToken)
            .ConfigureAwait(false);
        await notificationPublisher.PublishTodoReminderAsync(
            candidate.InstanceId, candidate.TodoId, candidate.AssigneeUserId,
            candidate.BusinessType, candidate.BusinessId, nextCount, now, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    /// <summary>追加不含表单内容的超时轨迹。</summary>
    /// <param name="candidate">超时扫描候选。</param>
    /// <param name="transitionKey">稳定迁移键。</param>
    /// <param name="now">发生时间。</param>
    /// <param name="cancellationToken">取消写入的令牌。</param>
    private Task<int> WriteLogAsync(
        WorkflowTodoTimeoutCandidateRecord candidate,
        string transitionKey,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        commandExecutor.ExecuteAsync(
            WorkflowSql.InsertExecutionLog,
            WorkflowSqlParameters.Create(
                ("Id", idGenerator.NewId()), ("InstanceId", candidate.InstanceId),
                ("StepId", candidate.StepId), ("TransitionKey", transitionKey),
                ("FromStatusKey", "active"), ("ToStatusKey", "active"),
                ("IdempotencyKey", null), ("Summary", null), ("CreatedAtUtc", now)),
            cancellationToken);

    /// <summary>返回两个可空时间中的较早者。</summary>
    /// <param name="left">第一个时间。</param>
    /// <param name="right">第二个时间。</param>
    /// <returns>较早的非空时间。</returns>
    private static DateTimeOffset? Min(DateTimeOffset? left, DateTimeOffset? right) =>
        left is null ? right : right is null ? left : left <= right ? left : right;
}

/// <summary>在 Hosted Service 的短作用域之间保存超时扫描位置，避免无效租户候选造成饥饿。</summary>
internal sealed class WorkflowTodoTimeoutScanCursor
{
    private readonly object _sync = new();
    private WorkflowTodoTimeoutCursor _value;

    /// <summary>读取当前不可变游标快照。</summary>
    /// <returns>本轮扫描起点。</returns>
    public WorkflowTodoTimeoutCursor Read()
    {
        lock (_sync)
        {
            return _value;
        }
    }

    /// <summary>满页时推进到最后候选；到达尾页时回绕到起点。</summary>
    /// <param name="candidates">按稳定顺序返回的候选。</param>
    /// <param name="batchSize">当前扫描批大小。</param>
    public void Advance(
        IReadOnlyList<WorkflowTodoTimeoutCandidateRecord> candidates,
        int batchSize)
    {
        lock (_sync)
        {
            if (candidates.Count == batchSize)
            {
                var last = candidates[^1];
                _value = new(last.NextTimeoutSignalAtUtc, last.TodoId);
                return;
            }

            // 已到扫描尾部，下轮从头开始；停用租户只会影响本轮，不会永久挡住后续候选。
            _value = default;
        }
    }
}

/// <summary>描述超时扫描的稳定复合游标。</summary>
/// <param name="SignalAtUtc">上一页最后信号时间。</param>
/// <param name="TodoId">同一信号时间内的最后待办标识。</param>
internal readonly record struct WorkflowTodoTimeoutCursor(
    DateTimeOffset? SignalAtUtc,
    Guid? TodoId);
