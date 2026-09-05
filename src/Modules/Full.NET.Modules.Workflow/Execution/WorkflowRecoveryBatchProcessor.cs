using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>
/// 短事务领取恢复任务，续租后在同一 Workflow 本地事务内修复或死信暂停，禁止 Jobs 直写 Workflow 表。
/// </summary>
/// <param name="queryExecutor">读取实例、待办和恢复任务的查询执行器。</param>
/// <param name="commandExecutor">完成、重排队和暂停实例的命令执行器。</param>
/// <param name="transaction">领取与处理共用的命令事务。</param>
/// <param name="clock">UTC 时钟。</param>
/// <param name="idGenerator">生成本轮租约持有者键。</param>
/// <param name="databaseOptions">当前数据库提供程序。</param>
/// <param name="workerOptions">批大小、租约与退避选项。</param>
/// <param name="logger">记录领取失败的日志器。</param>
internal sealed class WorkflowRecoveryBatchProcessor(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions,
    IOptions<WorkflowRecoveryWorkerOptions> workerOptions,
    ILogger<WorkflowRecoveryBatchProcessor> logger)
{
    private readonly WorkflowRecoveryWorkerOptions _options = workerOptions.Value;

    /// <summary>领取到期任务并逐条处理；过期租约可被其他 Worker 重领。</summary>
    /// <param name="cancellationToken">取消当前批次的令牌。</param>
    /// <returns>本批领取数量。</returns>
    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var leaseOwner = idGenerator.NewId().ToString("N");
        var now = clock.UtcNow;
        var claimed = await ClaimAsync(
                leaseOwner,
                now,
                now.AddSeconds(_options.LeaseSeconds),
                cancellationToken)
            .ConfigureAwait(false);
        foreach (var task in claimed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await transaction.ExecuteAsync(
                        token => ProcessOneAsync(task, leaseOwner, token),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Workflow recovery task {TaskId} failed.", task.Id);
            }
        }

        return claimed.Count;
    }

    /// <summary>按提供程序领取到期任务；MySQL 必须在事务内 SKIP LOCKED 后再按标识认领。</summary>
    /// <param name="leaseOwner">本轮 Worker 租约持有者键。</param>
    /// <param name="now">当前 UTC 时间。</param>
    /// <param name="leaseExpiresAt">领取后的租约过期时间。</param>
    /// <param name="cancellationToken">取消当前领取的令牌。</param>
    /// <returns>本批已领取任务。</returns>
    private async Task<IReadOnlyList<WorkflowRecoveryTaskRecord>> ClaimAsync(
        string leaseOwner,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken)
    {
        var batchSize = Math.Clamp(_options.BatchSize, 1, 50);
        if (databaseOptions.Value.Provider == DatabaseProvider.SqlServer)
        {
            return await queryExecutor.QueryAsync<WorkflowRecoveryTaskRecord>(
                    WorkflowRecoverySql.ClaimTasksSqlServer,
                    WorkflowSqlParameters.Create(
                        ("BatchSize", batchSize),
                        ("Now", now),
                        ("LeaseOwnerKey", leaseOwner),
                        ("LeaseExpiresAtUtc", leaseExpiresAt)),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await transaction.ExecuteAsync(
                async token =>
                {
                    var ids = await queryExecutor.QueryAsync<Guid>(
                            WorkflowRecoverySql.SelectClaimableTaskIdsMySql,
                            WorkflowSqlParameters.Create(("Now", now), ("BatchSize", batchSize)),
                            token)
                        .ConfigureAwait(false);
                    if (ids.Count == 0)
                    {
                        return Array.Empty<WorkflowRecoveryTaskRecord>();
                    }

                    await commandExecutor.ExecuteAsync(
                            WorkflowRecoverySql.ClaimTasksByIdsMySql,
                            WorkflowSqlParameters.Create(
                                ("LeaseOwnerKey", leaseOwner),
                                ("LeaseExpiresAtUtc", leaseExpiresAt),
                                ("Now", now),
                                ("Ids", ids.ToArray())),
                            token)
                        .ConfigureAwait(false);
                    return await queryExecutor.QueryAsync<WorkflowRecoveryTaskRecord>(
                            WorkflowRecoverySql.SelectTasksByLease,
                            WorkflowSqlParameters.Create(("LeaseOwnerKey", leaseOwner)),
                            token)
                        .ConfigureAwait(false);
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>在已领取租约内处理一条任务；丢失世代则放弃本轮提交。</summary>
    /// <param name="task">当前租约持有的恢复任务。</param>
    /// <param name="leaseOwner">本轮 Worker 租约持有者键。</param>
    /// <param name="cancellationToken">取消当前任务处理的令牌。</param>
    /// <returns>是否成功提交完成态。</returns>
    private async Task<bool> ProcessOneAsync(
        WorkflowRecoveryTaskRecord task,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (task.LeaseExpiresAtUtc is { } expires
            && expires - now <= TimeSpan.FromSeconds(_options.RenewWhenRemainingSeconds))
        {
            var renewed = await commandExecutor.ExecuteAsync(
                    WorkflowRecoverySql.RenewTaskLease,
                    WorkflowSqlParameters.Create(
                        ("Id", task.Id),
                        ("LeaseOwnerKey", leaseOwner),
                        ("LeaseGeneration", task.LeaseGeneration),
                        ("LeaseExpiresAtUtc", now.AddSeconds(_options.LeaseSeconds)),
                        ("Now", now)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (renewed != 1)
            {
                return false;
            }

            task = task with { LeaseGeneration = task.LeaseGeneration + 1 };
        }

        var instance = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
                WorkflowSql.FindInstanceById,
                WorkflowSqlParameters.Create(
                    ("Id", task.InstanceId),
                    ("TenantScopeKey", task.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        var activeTodo = await FindActiveTodoAsync(task, cancellationToken).ConfigureAwait(false);
        var attemptNumber = task.AttemptCount + 1;
        var category = Classify(instance, activeTodo);
        var (status, nextAttempt, suspendInstance) = WorkflowRecoveryRetry.ResolveOutcome(
            category,
            attemptNumber,
            now,
            _options);
        var lastError = status == WorkflowRecoveryStatuses.Succeeded
            ? null
            : Truncate($"kind={task.KindKey};instance={instance?.StatusKey ?? "missing"};todo={(activeTodo is null ? "none" : "active")}");

        if (suspendInstance && instance is { StatusKey: "active" })
        {
            var suspended = await commandExecutor.ExecuteAsync(
                    WorkflowSql.SuspendInstanceWithRevision,
                    WorkflowSqlParameters.Create(
                        ("Id", instance.Id),
                        ("TenantScopeKey", instance.TenantScopeKey),
                        ("Revision", instance.Revision)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (suspended != 1)
            {
                return false;
            }
        }
        else if (category == WorkflowRecoveryRetry.Succeeded && instance is { StatusKey: "active" })
        {
            await commandExecutor.ExecuteAsync(
                    WorkflowRecoverySql.ClearInstanceLease,
                    WorkflowSqlParameters.Create(
                        ("Id", instance.Id),
                        ("LeaseOwnerKey", leaseOwner),
                        ("Now", now)),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var completed = await commandExecutor.ExecuteAsync(
                WorkflowRecoverySql.CompleteTask,
                WorkflowSqlParameters.Create(
                    ("Id", task.Id),
                    ("StatusKey", status),
                    ("AttemptCount", attemptNumber),
                    ("LastError", lastError),
                    ("NextAttemptAtUtc", nextAttempt),
                    ("LeaseOwnerKey", leaseOwner),
                    ("LeaseGeneration", task.LeaseGeneration),
                    ("Now", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (completed != 1)
        {
            return false;
        }

        await commandExecutor.ExecuteAsync(
                WorkflowSql.InsertExecutionLog,
                WorkflowSqlParameters.Create(
                    ("Id", idGenerator.NewId()),
                    ("InstanceId", task.InstanceId),
                    ("StepId", task.StepId),
                    ("TransitionKey", $"recovery.{task.KindKey}"),
                    ("FromStatusKey", instance?.StatusKey),
                    ("ToStatusKey", suspendInstance ? "suspended" : instance?.StatusKey ?? "missing"),
                    ("IdempotencyKey", null),
                    ("Summary", lastError ?? "recovered"),
                    ("CreatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(
                WorkflowSql.InsertDomainAudit,
                WorkflowSqlParameters.Create(
                    ("Id", idGenerator.NewId()),
                    ("TenantId", task.TenantId),
                    ("ScopeKey", task.ScopeKey),
                    ("InstanceId", task.InstanceId),
                    ("OperationKey", $"recovery.{task.KindKey}"),
                    ("ActorUserId", Guid.Empty),
                    ("ResourceTypeKey", "recovery_task"),
                    ("ResourceId", task.Id),
                    ("OutcomeKey", status),
                    ("DetailJson", lastError),
                    ("CreatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    /// <summary>按任务种类查找对应活动待办；步骤级任务必须按 StepId 精确匹配。</summary>
    /// <param name="task">当前恢复任务。</param>
    /// <param name="cancellationToken">取消当前查询的令牌。</param>
    /// <returns>活动待办；不存在时为空。</returns>
    private Task<WorkflowTodoRecord?> FindActiveTodoAsync(
        WorkflowRecoveryTaskRecord task,
        CancellationToken cancellationToken) =>
        task.StepId is { } stepId
            ? queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoRecord>(
                WorkflowRecoverySql.FindActiveTodoByStep,
                WorkflowSqlParameters.Create(
                    ("StepId", stepId),
                    ("TenantScopeKey", task.TenantScopeKey)),
                cancellationToken)
            : queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoRecord>(
                WorkflowSql.FindActiveTodoByInstance,
                WorkflowSqlParameters.Create(
                    ("InstanceId", task.InstanceId),
                    ("TenantScopeKey", task.TenantScopeKey)),
                cancellationToken);

    /// <summary>
    /// 有活动待办或实例已离开 active 视为已修复；否则继续重试，耗尽后由调用方暂停实例。
    /// </summary>
    /// <param name="instance">当前作用域内的实例投影；缺失视为源条件消失。</param>
    /// <param name="activeTodo">对应步骤或实例上的活动待办。</param>
    /// <returns>闭合结果类别。</returns>
    private static string Classify(
        WorkflowInstanceRecord? instance,
        WorkflowTodoRecord? activeTodo)
    {
        if (instance is null || instance.StatusKey is "completed" or "rejected" or "cancelled" or "suspended")
        {
            return WorkflowRecoveryRetry.Succeeded;
        }

        return activeTodo is null ? WorkflowRecoveryRetry.Retryable : WorkflowRecoveryRetry.Succeeded;
    }

    /// <summary>截断写入 LastError 的摘要，避免超过 512 字符列宽。</summary>
    /// <param name="value">原始错误摘要。</param>
    /// <returns>不超过 512 字符的摘要。</returns>
    private static string Truncate(string value) =>
        value.Length <= 512 ? value : value[..512];
}
