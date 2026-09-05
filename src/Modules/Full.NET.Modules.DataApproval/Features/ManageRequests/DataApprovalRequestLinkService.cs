using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.DataApproval.Domain;
using Full.NET.Modules.DataApproval.Persistence;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.DataApproval.Features.ManageRequests;

/// <summary>幂等启动工作流并 CAS 关联 DataApproval 请求。</summary>
internal sealed class DataApprovalRequestLinkService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IWorkflowInstanceStarter workflowStarter)
{
    /// <summary>尝试为 pending 请求启动并关联工作流；失败时持久化可恢复状态。</summary>
    /// <param name="row">当前可信作用域内的请求行。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功时返回最新响应；不可恢复或并发冲突时返回失败 Result。</returns>
    public async Task<Result<DataApprovalRequestResponse>> TryLinkWorkflowAsync(
        DataApprovalRequestRecord row,
        CancellationToken cancellationToken = default)
    {
        if (row.WorkflowInstanceId is not null)
        {
            return Result<DataApprovalRequestResponse>.Success(DataApprovalRequestService.Map(row));
        }

        if (!DataApprovalRecoveryRules.CanRecoverWorkflowLink(
                row.StatusKey,
                row.WorkflowInstanceId,
                row.RecoveryStatusKey))
        {
            return Result<DataApprovalRequestResponse>.Failure(new Error(
                DataApprovalErrorCodes.RecoveryNotRetryable,
                "The approval request cannot recover workflow linking in the current state.",
                ErrorType.Conflict));
        }

        var catalogEntry = DataApprovalScenarioCatalog.Find(row.ScenarioKey);
        if (catalogEntry is null)
        {
            await RecordFailureAsync(
                    row,
                    DataApprovalRecoveryStatusKeys.FailedTerminal,
                    DataApprovalErrorCodes.ScenarioUnsupported,
                    "The approval scenario is not supported.",
                    cancellationToken)
                .ConfigureAwait(false);
            return Result<DataApprovalRequestResponse>.Failure(new Error(
                DataApprovalErrorCodes.ScenarioUnsupported,
                "The approval scenario is not supported.",
                ErrorType.Validation));
        }

        var now = clock.UtcNow;
        var start = await workflowStarter.StartAsync(
                row.SubmittedByUserId,
                new StartWorkflowInstanceCommand(
                    row.WorkflowDefinitionVersionId,
                    catalogEntry.WorkflowBusinessType,
                    row.Id.ToString("D"),
                    "{}",
                    $"{row.IdempotencyKey}:start",
                    DataApprovalScenarioCatalog.FormatWorkflowBusinessTitle(
                        row.ScenarioKey,
                        row.TargetEntityId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (!start.IsSuccess)
        {
            var updated = await RecordFailureAsync(
                    row,
                    DataApprovalRecoveryStatusKeys.FailedRetryable,
                    start.Error!.Code,
                    start.Error.Message,
                    cancellationToken)
                .ConfigureAwait(false);
            return updated is null
                ? Result<DataApprovalRequestResponse>.Failure(start.Error!)
                : Result<DataApprovalRequestResponse>.Success(DataApprovalRequestService.Map(updated));
        }

        var affected = await commandExecutor.ExecuteAsync(
                DataApprovalSql.LinkWorkflowInstance,
                DataApprovalSqlParameters.Create(
                    ("Id", row.Id),
                    ("TenantScopeKey", row.TenantScopeKey),
                    ("WorkflowInstanceId", start.Value!.InstanceId),
                    ("WorkflowRevision", start.Value.Revision),
                    ("StatusKey", DataApprovalStatusKeys.InReview),
                    ("RecoveryStatusKey", DataApprovalRecoveryStatusKeys.None),
                    ("UpdatedAtUtc", now),
                    ("ExpectedVersion", row.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            var latest = await queryExecutor.QuerySingleOrDefaultAsync<DataApprovalRequestRecord>(
                    DataApprovalSql.FindRequestById,
                    DataApprovalSqlParameters.Create(
                        ("Id", row.Id),
                        ("TenantScopeKey", row.TenantScopeKey)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (latest?.WorkflowInstanceId == start.Value.InstanceId)
            {
                return Result<DataApprovalRequestResponse>.Success(DataApprovalRequestService.Map(latest));
            }

            if (latest is not null)
            {
                latest = await RecordFailureAsync(
                        latest,
                        DataApprovalRecoveryStatusKeys.FailedRetryable,
                        DataApprovalErrorCodes.RecoveryRetryConflict,
                        "The approval request could not be linked to the workflow.",
                        cancellationToken)
                    .ConfigureAwait(false);
                if (latest is not null)
                {
                    return Result<DataApprovalRequestResponse>.Success(DataApprovalRequestService.Map(latest));
                }
            }

            return Result<DataApprovalRequestResponse>.Failure(new Error(
                DataApprovalErrorCodes.RecoveryRetryConflict,
                "The approval request could not be linked to the workflow.",
                ErrorType.Conflict));
        }

        var linked = await queryExecutor.QuerySingleOrDefaultAsync<DataApprovalRequestRecord>(
                DataApprovalSql.FindRequestById,
                DataApprovalSqlParameters.Create(
                    ("Id", row.Id),
                    ("TenantScopeKey", row.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        return linked is null
            ? Result<DataApprovalRequestResponse>.Failure(new Error(
                DataApprovalErrorCodes.RequestNotFound,
                "The approval request was not found.",
                ErrorType.NotFound))
            : Result<DataApprovalRequestResponse>.Success(DataApprovalRequestService.Map(linked));
    }

    private async Task<DataApprovalRequestRecord?> RecordFailureAsync(
        DataApprovalRequestRecord row,
        string recoveryStatusKey,
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
                DataApprovalSql.RecordRecoveryFailure,
                DataApprovalSqlParameters.Create(
                    ("Id", row.Id),
                    ("TenantScopeKey", row.TenantScopeKey),
                    ("RecoveryStatusKey", recoveryStatusKey),
                    ("LastFailureCode", failureCode),
                    ("LastFailureMessage", message),
                    ("LastRecoveryAttemptAtUtc", now),
                    ("UpdatedAtUtc", now),
                    ("ExpectedVersion", row.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return await queryExecutor.QuerySingleOrDefaultAsync<DataApprovalRequestRecord>(
                    DataApprovalSql.FindRequestById,
                    DataApprovalSqlParameters.Create(
                        ("Id", row.Id),
                        ("TenantScopeKey", row.TenantScopeKey)),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await queryExecutor.QuerySingleOrDefaultAsync<DataApprovalRequestRecord>(
                DataApprovalSql.FindRequestById,
                DataApprovalSqlParameters.Create(
                    ("Id", row.Id),
                    ("TenantScopeKey", row.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
