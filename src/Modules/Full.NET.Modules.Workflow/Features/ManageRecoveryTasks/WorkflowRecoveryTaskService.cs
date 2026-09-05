using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Features.ManageRecoveryTasks;

/// <summary>在可信作用域内查询恢复任务，并按修订号与幂等键执行人工重试或对账。</summary>
/// <param name="queryExecutor">读取恢复任务、实例和待办的查询执行器。</param>
/// <param name="commandExecutor">重试、对账和审计写入执行器。</param>
/// <param name="transaction">管理写操作共用的命令事务。</param>
/// <param name="currentTenant">可信当前作用域。</param>
/// <param name="clock">UTC 时钟。</param>
/// <param name="idGenerator">生成审计记录标识。</param>
/// <param name="databaseOptions">当前数据库提供程序。</param>
internal sealed class WorkflowRecoveryTaskService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>分页列出当前作用域内的恢复任务。</summary>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="pageSize">每页条数，限制在 1～100。</param>
    /// <param name="cancellationToken">取消当前查询的令牌。</param>
    /// <returns>当前页任务或稳定业务错误。</returns>
    public async Task<Result<PagedResult<WorkflowRecoveryTaskResponse>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                WorkflowRecoverySql.CountTasksForScope,
                WorkflowSqlParameters.Create(("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider == DatabaseProvider.MySql
            ? WorkflowRecoverySql.ListTasksForScopeMySql
            : WorkflowRecoverySql.ListTasksForScopeSqlServer;
        var rows = await queryExecutor.QueryAsync<WorkflowRecoveryTaskRecord>(
                statement,
                WorkflowSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("Offset", offset),
                    ("PageSize", pageSize)),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<WorkflowRecoveryTaskResponse>>.Success(
            new PagedResult<WorkflowRecoveryTaskResponse>(
                rows.Select(Map).ToArray(),
                page,
                pageSize,
                total));
    }

    /// <summary>读取当前作用域内的单条恢复任务。</summary>
    /// <param name="taskId">恢复任务标识。</param>
    /// <param name="cancellationToken">取消当前查询的令牌。</param>
    /// <returns>任务快照或不存在错误。</returns>
    public async Task<Result<WorkflowRecoveryTaskResponse>> GetAsync(
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var record = await FindAsync(taskId, cancellationToken).ConfigureAwait(false);
        return record is null
            ? NotFound()
            : Result<WorkflowRecoveryTaskResponse>.Success(Map(record));
    }

    /// <summary>把失败或死信任务重新入队，重置尝试次数并立即允许 Worker 领取。</summary>
    /// <param name="taskId">恢复任务标识。</param>
    /// <param name="actorUserId">操作人标识。</param>
    /// <param name="request">修订号、原因和幂等键。</param>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
    /// <returns>重试后的任务快照或稳定业务错误。</returns>
    public Task<Result<WorkflowRecoveryTaskResponse>> RetryAsync(
        Guid taskId,
        Guid actorUserId,
        RetryWorkflowRecoveryTaskRequest request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            taskId,
            actorUserId,
            request.ExpectedRevision,
            request.Reason,
            request.IdempotencyKey,
            "recovery.retry",
            requireReason: true,
            RetryCoreAsync,
            cancellationToken);

    /// <summary>根据当前实例与待办事实关闭已修复任务；源条件仍在时拒绝自动收敛。</summary>
    /// <param name="taskId">恢复任务标识。</param>
    /// <param name="actorUserId">操作人标识。</param>
    /// <param name="request">修订号、可选原因和幂等键。</param>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
    /// <returns>对账后的任务快照或稳定业务错误。</returns>
    public Task<Result<WorkflowRecoveryTaskResponse>> ReconcileAsync(
        Guid taskId,
        Guid actorUserId,
        ReconcileWorkflowRecoveryTaskRequest request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            taskId,
            actorUserId,
            request.ExpectedRevision,
            request.Reason,
            request.IdempotencyKey,
            "recovery.reconcile",
            requireReason: false,
            ReconcileCoreAsync,
            cancellationToken);

    private async Task<Result<WorkflowRecoveryTaskResponse>> MutateAsync(
        Guid taskId,
        Guid actorUserId,
        long expectedRevision,
        string? reason,
        string idempotencyKey,
        string actionKey,
        bool requireReason,
        Func<Guid, Guid, long, string?, string, string, WorkflowManagementScope, CancellationToken, Task<Result<WorkflowRecoveryTaskResponse>>> core,
        CancellationToken cancellationToken)
    {
        if (taskId == Guid.Empty || actorUserId == Guid.Empty ||
            !IsValid(expectedRevision, reason, idempotencyKey, requireReason))
        {
            return Failure(
                actionKey == "recovery.retry"
                    ? WorkflowErrorCodes.RecoveryRetryInvalid
                    : WorkflowErrorCodes.RecoveryReconcileInvalid,
                ErrorType.Validation);
        }

        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var normalizedKey = idempotencyKey.Trim();
        var requestHash = HashRequest(actionKey, expectedRevision, reason);
        try
        {
            var result = await transaction.ExecuteResultAsync(
                    token => core(
                        taskId, actorUserId, expectedRevision, reason, normalizedKey, requestHash, scope, token),
                    cancellationToken)
                .ConfigureAwait(false);
            return !result.IsSuccess && result.Error?.Code == WorkflowErrorCodes.RevisionConflict
                ? await ResolveReplayAsync(taskId, actorUserId, normalizedKey, actionKey, requestHash, cancellationToken)
                    .ConfigureAwait(false)
                : result;
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return await ResolveReplayAsync(taskId, actorUserId, normalizedKey, actionKey, requestHash, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<Result<WorkflowRecoveryTaskResponse>> RetryCoreAsync(
        Guid taskId,
        Guid actorUserId,
        long expectedRevision,
        string? reason,
        string idempotencyKey,
        string requestHash,
        WorkflowManagementScope scope,
        CancellationToken cancellationToken)
    {
        var existing = await FindAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        var replay = await TryReplayAsync(
                taskId, existing.InstanceId, actorUserId, idempotencyKey, "recovery.retry", requestHash, cancellationToken)
            .ConfigureAwait(false);
        if (replay is not null)
        {
            return replay;
        }

        if (existing.StatusKey is not (WorkflowRecoveryStatuses.Failed or WorkflowRecoveryStatuses.DeadLettered))
        {
            return Failure(WorkflowErrorCodes.RecoveryRetryInvalid, ErrorType.Validation);
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                WorkflowRecoverySql.RequeueTask,
                WorkflowSqlParameters.Create(
                    ("Id", taskId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("Revision", expectedRevision),
                    ("NextAttemptAtUtc", now),
                    ("Now", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        await WriteReceiptAsync(
                existing, actorUserId, expectedRevision, idempotencyKey, reason, "recovery.retry",
                requestHash, "pending", now, cancellationToken)
            .ConfigureAwait(false);
        return await GetAsync(taskId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<WorkflowRecoveryTaskResponse>> ReconcileCoreAsync(
        Guid taskId,
        Guid actorUserId,
        long expectedRevision,
        string? reason,
        string idempotencyKey,
        string requestHash,
        WorkflowManagementScope scope,
        CancellationToken cancellationToken)
    {
        var existing = await FindAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        var replay = await TryReplayAsync(
                taskId, existing.InstanceId, actorUserId, idempotencyKey, "recovery.reconcile", requestHash, cancellationToken)
            .ConfigureAwait(false);
        if (replay is not null)
        {
            return replay;
        }

        if (existing.StatusKey is WorkflowRecoveryStatuses.Succeeded or WorkflowRecoveryStatuses.Cancelled)
        {
            return Failure(WorkflowErrorCodes.RecoveryReconcileInvalid, ErrorType.Validation);
        }

        var instance = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
                WorkflowSql.FindInstanceById,
                WorkflowSqlParameters.Create(("Id", existing.InstanceId), ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        var activeTodo = existing.StepId is { } stepId
            ? await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoRecord>(
                    WorkflowRecoverySql.FindActiveTodoByStep,
                    WorkflowSqlParameters.Create(("StepId", stepId), ("TenantScopeKey", scope.TenantScopeKey)),
                    cancellationToken)
                .ConfigureAwait(false)
            : await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoRecord>(
                    WorkflowSql.FindActiveTodoByInstance,
                    WorkflowSqlParameters.Create(("InstanceId", existing.InstanceId), ("TenantScopeKey", scope.TenantScopeKey)),
                    cancellationToken)
                .ConfigureAwait(false);

        var closeStatus = ResolveReconcileStatus(instance, activeTodo);
        if (closeStatus is null)
        {
            return Failure(WorkflowErrorCodes.RecoveryReconcileInvalid, ErrorType.Validation);
        }

        var now = clock.UtcNow;
        var statement = closeStatus == WorkflowRecoveryStatuses.Cancelled
            ? WorkflowRecoverySql.MarkTaskCancelled
            : WorkflowRecoverySql.MarkTaskSucceeded;
        var affected = await commandExecutor.ExecuteAsync(
                statement,
                WorkflowSqlParameters.Create(
                    ("Id", taskId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("Revision", expectedRevision),
                    ("Now", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        if (closeStatus == WorkflowRecoveryStatuses.Succeeded && instance is { StatusKey: "active" })
        {
            await commandExecutor.ExecuteAsync(
                    WorkflowRecoverySql.ClearInstanceLease,
                    WorkflowSqlParameters.Create(
                        ("Id", instance.Id),
                        ("LeaseOwnerKey", existing.LeaseOwnerKey),
                        ("Now", now)),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await WriteReceiptAsync(
                existing, actorUserId, expectedRevision, idempotencyKey, reason, "recovery.reconcile",
                requestHash, closeStatus, now, cancellationToken)
            .ConfigureAwait(false);
        return await GetAsync(taskId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<WorkflowRecoveryTaskResponse>?> TryReplayAsync(
        Guid taskId,
        Guid instanceId,
        Guid actorUserId,
        string idempotencyKey,
        string actionKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var receipt = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
                WorkflowSql.FindActionReceipt,
                WorkflowSqlParameters.Create(("InstanceId", instanceId), ("IdempotencyKey", idempotencyKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (receipt is null)
        {
            return null;
        }

        if (!string.Equals(receipt.ActionKey, actionKey, StringComparison.Ordinal)
            || receipt.ActorUserId != actorUserId
            || !string.Equals(receipt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
        }

        return await GetAsync(taskId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<WorkflowRecoveryTaskResponse>> ResolveReplayAsync(
        Guid taskId,
        Guid actorUserId,
        string idempotencyKey,
        string actionKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var current = await FindAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return NotFound();
        }

        return await TryReplayAsync(
                taskId, current.InstanceId, actorUserId, idempotencyKey, actionKey, requestHash, cancellationToken)
            .ConfigureAwait(false)
            ?? Failure(WorkflowErrorCodes.RevisionConflict, ErrorType.Conflict);
    }

    private Task<WorkflowRecoveryTaskRecord?> FindAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var scope = WorkflowManagementScope.Resolve(currentTenant);
        return queryExecutor.QuerySingleOrDefaultAsync<WorkflowRecoveryTaskRecord>(
            WorkflowRecoverySql.FindTaskById,
            WorkflowSqlParameters.Create(("Id", taskId), ("TenantScopeKey", scope.TenantScopeKey)),
            cancellationToken);
    }

    private async Task WriteReceiptAsync(
        WorkflowRecoveryTaskRecord task,
        Guid actorUserId,
        long instanceRevision,
        string idempotencyKey,
        string? reason,
        string actionKey,
        string requestHash,
        string outcomeKey,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await commandExecutor.ExecuteAsync(
                WorkflowSql.InsertActionRecord,
                WorkflowSqlParameters.Create(
                    ("Id", idGenerator.NewId()),
                    ("InstanceId", task.InstanceId),
                    ("StepId", task.StepId),
                    ("TodoId", null),
                    ("ActionKey", actionKey),
                    ("ActorUserId", actorUserId),
                    ("InstanceRevision", instanceRevision),
                    ("IdempotencyKey", idempotencyKey),
                    ("CommentSummary", string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()),
                    ("CreatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(
                WorkflowSql.InsertExecutionLog,
                WorkflowSqlParameters.Create(
                    ("Id", idGenerator.NewId()),
                    ("InstanceId", task.InstanceId),
                    ("StepId", task.StepId),
                    ("TransitionKey", actionKey),
                    ("FromStatusKey", task.StatusKey),
                    ("ToStatusKey", outcomeKey),
                    ("IdempotencyKey", idempotencyKey),
                    ("Summary", requestHash),
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
                    ("OperationKey", actionKey),
                    ("ActorUserId", actorUserId),
                    ("ResourceTypeKey", "recovery_task"),
                    ("ResourceId", task.Id),
                    ("OutcomeKey", outcomeKey),
                    ("DetailJson", string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()),
                    ("CreatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>终态实例关闭为 cancelled；已暂停或活动待办已补齐则 succeeded；仍卡住则无法对账。</summary>
    private static string? ResolveReconcileStatus(
        WorkflowInstanceRecord? instance,
        WorkflowTodoRecord? activeTodo)
    {
        if (instance is null || instance.StatusKey is "completed" or "rejected" or "cancelled")
        {
            return WorkflowRecoveryStatuses.Cancelled;
        }

        if (instance.StatusKey == "suspended" || activeTodo is not null)
        {
            return WorkflowRecoveryStatuses.Succeeded;
        }

        return null;
    }

    private static WorkflowRecoveryTaskResponse Map(WorkflowRecoveryTaskRecord record) =>
        new(
            record.Id,
            record.InstanceId,
            record.StepId,
            record.KindKey,
            record.StatusKey,
            record.AttemptCount,
            record.Revision,
            record.LeaseOwnerKey,
            record.LeaseExpiresAtUtc,
            record.LeaseGeneration,
            record.NextAttemptAtUtc,
            record.LastError,
            record.CreatedAtUtc,
            record.UpdatedAtUtc);

    private static bool IsValid(long expectedRevision, string? reason, string idempotencyKey, bool requireReason)
    {
        if (expectedRevision < 1
            || idempotencyKey.Trim() is not { Length: >= 1 and <= 128 }
            || reason?.Trim() is { Length: > 512 })
        {
            return false;
        }

        return !requireReason || reason?.Trim() is { Length: > 0 };
    }

    private static string HashRequest(string actionKey, long expectedRevision, string? reason)
    {
        var normalized = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{actionKey}\n{expectedRevision}\n{normalized}")));
    }

    private static Result<WorkflowRecoveryTaskResponse> NotFound() =>
        Failure(WorkflowErrorCodes.RecoveryNotFound, ErrorType.NotFound);

    private static Result<WorkflowRecoveryTaskResponse> Failure(string code, ErrorType type) =>
        Result<WorkflowRecoveryTaskResponse>.Failure(
            new Error(code, "The workflow recovery task operation failed.", type));
}
