using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.DataApproval.Domain;
using Full.NET.Modules.DataApproval.Features.ManageRequests;
using Full.NET.Modules.DataApproval.Persistence;

namespace Full.NET.Modules.DataApproval.Features.ProjectWorkflowOutcomes;

/// <summary>在审批通过后幂等应用业务变更，并持久化可恢复的应用结果。</summary>
internal sealed class DataApprovalRequestApplicationService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IEnumerable<IDataApprovalScenarioHandler> scenarioHandlers)
{
    /// <summary>尝试将已批准快照应用到目标实体；失败时保持 in_review 并记录应用状态。</summary>
    /// <param name="row">当前可信作用域内的请求行。</param>
    /// <param name="actorUserId">执行应用的用户标识。</param>
    /// <param name="idempotencyKey">调用方幂等键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功时返回 approved 响应；可恢复失败时返回带应用失败字段的 in_review 响应。</returns>
    public async Task<Result<DataApprovalRequestResponse>> TryApplyApprovedChangeAsync(
        DataApprovalRequestRecord row,
        Guid actorUserId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(row.StatusKey, DataApprovalStatusKeys.Approved, StringComparison.Ordinal) &&
            string.Equals(row.ApplicationStatusKey, DataApprovalApplicationStatusKeys.Applied, StringComparison.Ordinal))
        {
            return Result<DataApprovalRequestResponse>.Success(DataApprovalRequestService.Map(row));
        }

        if (!DataApprovalApplicationRules.CanApply(row.StatusKey, row.ApplicationStatusKey))
        {
            return Result<DataApprovalRequestResponse>.Failure(new Error(
                DataApprovalErrorCodes.ApplicationNotRetryable,
                "The approval request cannot apply the approved change in the current state.",
                ErrorType.Conflict));
        }

        var pendingRow = await EnsurePendingApplyAsync(row, cancellationToken).ConfigureAwait(false);
        if (pendingRow is null)
        {
            return Result<DataApprovalRequestResponse>.Failure(new Error(
                DataApprovalErrorCodes.ApplicationRetryConflict,
                "The approval request changed concurrently before application.",
                ErrorType.Conflict));
        }

        row = pendingRow;
        var scenario = scenarioHandlers.SingleOrDefault(handler => handler.Supports(row.ScenarioKey));
        var apply = scenario is null
            ? Result<bool>.Failure(new Error(DataApprovalErrorCodes.ScenarioUnsupported,
                "The approval scenario provider is unavailable.", ErrorType.Validation))
            : await scenario.ApplyAsync(row.ScenarioKey, row.TargetEntityId, row.AfterSnapshotJson,
                actorUserId, idempotencyKey, cancellationToken).ConfigureAwait(false);

        if (apply.IsSuccess)
        {
            return await CompleteApprovedApplicationAsync(row, cancellationToken).ConfigureAwait(false);
        }

        var applicationStatusKey = apply.Error!.Type == ErrorType.Validation
            ? DataApprovalApplicationStatusKeys.FailedTerminal
            : DataApprovalApplicationStatusKeys.FailedRetryable;
        var updated = await RecordApplicationFailureAsync(
                row,
                applicationStatusKey,
                apply.Error!.Code,
                apply.Error.Message,
                cancellationToken)
            .ConfigureAwait(false);
        return updated is null
            ? Result<DataApprovalRequestResponse>.Failure(apply.Error!)
            : Result<DataApprovalRequestResponse>.Success(DataApprovalRequestService.Map(updated));
    }

    private async Task<DataApprovalRequestRecord?> EnsurePendingApplyAsync(
        DataApprovalRequestRecord row,
        CancellationToken cancellationToken)
    {
        if (string.Equals(row.ApplicationStatusKey, DataApprovalApplicationStatusKeys.PendingApply, StringComparison.Ordinal) ||
            string.Equals(row.ApplicationStatusKey, DataApprovalApplicationStatusKeys.FailedRetryable, StringComparison.Ordinal))
        {
            return row;
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                DataApprovalSql.MarkApplicationPending,
                DataApprovalSqlParameters.Create(
                    ("Id", row.Id),
                    ("TenantScopeKey", row.TenantScopeKey),
                    ("ApplicationStatusKey", DataApprovalApplicationStatusKeys.PendingApply),
                    ("UpdatedAtUtc", now),
                    ("ExpectedStatusKey", DataApprovalStatusKeys.InReview),
                    ("ExpectedVersion", row.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 1)
        {
            return await ReloadAsync(row, cancellationToken).ConfigureAwait(false);
        }

        return await ReloadAsync(row, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<Result<DataApprovalRequestResponse>> CompleteApprovedApplicationAsync(
        DataApprovalRequestRecord row,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                DataApprovalSql.CompleteApprovedApplication,
                DataApprovalSqlParameters.Create(
                    ("Id", row.Id),
                    ("TenantScopeKey", row.TenantScopeKey),
                    ("StatusKey", DataApprovalStatusKeys.Approved),
                    ("ApplicationStatusKey", DataApprovalApplicationStatusKeys.Applied),
                    ("ApplicationAttemptIncrement", 1),
                    ("LastApplicationAttemptAtUtc", now),
                    ("ResolvedAtUtc", now),
                    ("UpdatedAtUtc", now),
                    ("ExpectedStatusKey", DataApprovalStatusKeys.InReview),
                    ("ExpectedVersion", row.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            var latest = await ReloadAsync(row, cancellationToken).ConfigureAwait(false);
            return latest is not null &&
                   string.Equals(latest.StatusKey, DataApprovalStatusKeys.Approved, StringComparison.Ordinal)
                ? Result<DataApprovalRequestResponse>.Success(DataApprovalRequestService.Map(latest))
                : Result<DataApprovalRequestResponse>.Failure(new Error(
                    DataApprovalErrorCodes.ApplicationRetryConflict,
                    "The approval request could not be marked as approved after application.",
                    ErrorType.Conflict));
        }

        var completed = await ReloadAsync(row, cancellationToken).ConfigureAwait(false);
        return completed is null
            ? Result<DataApprovalRequestResponse>.Failure(new Error(
                DataApprovalErrorCodes.RequestNotFound,
                "The approval request was not found.",
                ErrorType.NotFound))
            : Result<DataApprovalRequestResponse>.Success(DataApprovalRequestService.Map(completed));
    }

    private async Task<DataApprovalRequestRecord?> RecordApplicationFailureAsync(
        DataApprovalRequestRecord row,
        string applicationStatusKey,
        string failureCode,
        string? failureMessage,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var message = failureMessage?.Trim();
        if (message?.Length > 512)
        {
            message = message[..512];
        }

        var affected = await commandExecutor.ExecuteAsync(
                DataApprovalSql.RecordApplicationFailure,
                DataApprovalSqlParameters.Create(
                    ("Id", row.Id),
                    ("TenantScopeKey", row.TenantScopeKey),
                    ("ApplicationStatusKey", applicationStatusKey),
                    ("LastApplicationFailureCode", failureCode),
                    ("LastApplicationFailureMessage", message),
                    ("LastApplicationAttemptAtUtc", now),
                    ("UpdatedAtUtc", now),
                    ("ExpectedStatusKey", DataApprovalStatusKeys.InReview),
                    ("ExpectedVersion", row.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        return affected == 1
            ? await ReloadAsync(row, cancellationToken).ConfigureAwait(false)
            : await ReloadAsync(row, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DataApprovalRequestRecord?> ReloadAsync(
        DataApprovalRequestRecord row,
        CancellationToken cancellationToken) =>
        await queryExecutor.QuerySingleOrDefaultAsync<DataApprovalRequestRecord>(
                DataApprovalSql.FindRequestById,
                DataApprovalSqlParameters.Create(
                    ("Id", row.Id),
                    ("TenantScopeKey", row.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
}
