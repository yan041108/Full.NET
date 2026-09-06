using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.DataApproval.Domain;
using Full.NET.Modules.DataApproval.Features.ManageScenarios;
using Full.NET.Modules.DataApproval.Features.ProjectWorkflowOutcomes;
using Full.NET.Modules.DataApproval.Persistence;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.DataApproval.Features.ManageRequests;

/// <summary>管理 DataApproval 请求的创建、查询与取消。</summary>
internal sealed class DataApprovalRequestService(
    IQueryExecutor queryExecutor,
    IMultiResultQueryExecutor multiResultQueryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions,
    ISerialRuleChangeApprovalSource serialRuleApprovalSource,
    DataApprovalScenarioService scenarioService,
    IWorkflowInstanceCanceller workflowCanceller,
    DataApprovalRequestLinkService linkService,
    DataApprovalRequestApplicationService applicationService)
{
    /// <summary>分页查询审批请求。</summary>
    public async Task<Result<PagedResult<DataApprovalRequestResponse>>> ListAsync(
        int page,
        int pageSize,
        string? scenarioKey,
        string? statusKey,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var scope = DataApprovalManagementScope.Resolve(currentTenant);
        var offset = ((long)page - 1) * pageSize;
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => DataApprovalSql.PageRequestsSqlServer,
            DatabaseProvider.MySql => DataApprovalSql.PageRequestsMySql,
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };
        var result = await multiResultQueryExecutor.QueryMultipleAsync(
                statement,
                DataApprovalSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("ScenarioKey", NormalizeOptional(scenarioKey)),
                    ("StatusKey", NormalizeOptional(statusKey)),
                    ("Offset", offset),
                    ("PageSize", pageSize)),
                async (reader, _) =>
                {
                    var rows = await reader.ReadAsync<DataApprovalRequestRecord>()
                        .ConfigureAwait(false);
                    var total = await reader.ReadSingleOrDefaultAsync<long>().ConfigureAwait(false);
                    return (Rows: rows.ToArray(), Total: total);
                },
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<DataApprovalRequestResponse>>.Success(
            new PagedResult<DataApprovalRequestResponse>(
                result.Rows.Select(Map).ToArray(),
                page,
                pageSize,
                result.Total));
    }

    /// <summary>读取单个审批请求。</summary>
    public async Task<Result<DataApprovalRequestResponse>> GetAsync(
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        var row = await FindAsync(requestId, cancellationToken).ConfigureAwait(false);
        return row is null
            ? NotFound()
            : Result<DataApprovalRequestResponse>.Success(Map(row));
    }

    /// <summary>创建审批请求并在支持的场景下启动工作流。</summary>
    public async Task<Result<DataApprovalRequestResponse>> CreateAsync(
        Guid actorUserId,
        CreateDataApprovalRequestBody request,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeCreate(request, out var normalized, out var error))
        {
            return Result<DataApprovalRequestResponse>.Failure(error!);
        }

        var scope = DataApprovalManagementScope.Resolve(currentTenant);
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<DataApprovalRequestRecord>(
                DataApprovalSql.FindRequestByIdempotency,
                DataApprovalSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("IdempotencyKey", normalized.IdempotencyKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return await linkService.TryLinkWorkflowAsync(existing, cancellationToken)
                .ConfigureAwait(false);
        }

        var beforeSnapshotJson = (string?)null;
        if (RequiresSerialRuleBeforeSnapshot(normalized.ScenarioKey))
        {
            var snapshot = await serialRuleApprovalSource
                .GetSnapshotAsync(normalized.TargetEntityId, cancellationToken)
                .ConfigureAwait(false);
            if (!snapshot.IsSuccess)
            {
                return Result<DataApprovalRequestResponse>.Failure(snapshot.Error!);
            }

            beforeSnapshotJson = snapshot.Value!.SnapshotJson;
        }

        var binding = await scenarioService
            .ResolveForCreateAsync(normalized.ScenarioKey, cancellationToken)
            .ConfigureAwait(false);
        if (!binding.IsSuccess)
        {
            return Result<DataApprovalRequestResponse>.Failure(binding.Error!);
        }

        var definitionVersionId = binding.Value!.WorkflowDefinitionVersionId;
        var requestId = idGenerator.NewId();
        var now = clock.UtcNow;
        try
        {
            var created = await transaction.ExecuteResultAsync(async token =>
            {
                await commandExecutor.ExecuteAsync(
                    DataApprovalSql.InsertRequest,
                    DataApprovalSqlParameters.Create(
                        ("Id", requestId),
                        ("TenantId", scope.TenantId),
                        ("ScopeKey", scope.ScopeKey),
                        ("TenantScopeKey", scope.TenantScopeKey),
                        ("ScenarioKey", normalized.ScenarioKey),
                        ("TargetEntityId", normalized.TargetEntityId),
                        ("StatusKey", DataApprovalStatusKeys.Pending),
                        ("BeforeSnapshotJson", beforeSnapshotJson),
                        ("AfterSnapshotJson", normalized.ProposedChangeJson),
                        ("WorkflowInstanceId", null),
                        ("WorkflowRevision", null),
                        ("WorkflowDefinitionVersionId", definitionVersionId),
                        ("SubmittedByUserId", actorUserId),
                        ("SubmittedAtUtc", now),
                        ("ResolvedAtUtc", null),
                        ("IdempotencyKey", normalized.IdempotencyKey),
                        ("RecoveryStatusKey", DataApprovalRecoveryStatusKeys.PendingLink),
                        ("LastFailureCode", null),
                        ("LastFailureMessage", null),
                        ("LastRecoveryAttemptAtUtc", null),
                        ("RecoveryAttemptCount", 0),
                        ("ApplicationStatusKey", DataApprovalApplicationStatusKeys.None),
                        ("LastApplicationFailureCode", null),
                        ("LastApplicationFailureMessage", null),
                        ("LastApplicationAttemptAtUtc", null),
                        ("ApplicationAttemptCount", 0),
                        ("CreatedAtUtc", now),
                        ("UpdatedAtUtc", now),
                        ("Version", 1L)),
                    token).ConfigureAwait(false);

                var row = await queryExecutor.QuerySingleOrDefaultAsync<DataApprovalRequestRecord>(
                        DataApprovalSql.FindRequestById,
                        DataApprovalSqlParameters.Create(
                            ("Id", requestId),
                            ("TenantScopeKey", scope.TenantScopeKey)),
                        token)
                    .ConfigureAwait(false);
                return row is null
                    ? NotFound()
                    : Result<DataApprovalRequestResponse>.Success(Map(row));
            }, cancellationToken).ConfigureAwait(false);
            if (!created.IsSuccess)
            {
                return created;
            }

            var createdRow = await FindAsync(requestId, cancellationToken).ConfigureAwait(false);
            return createdRow is null
                ? NotFound()
                : await linkService.TryLinkWorkflowAsync(createdRow, cancellationToken).ConfigureAwait(false);
        }
        catch (DataCommandException exception)
            when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            var replay = await queryExecutor.QuerySingleOrDefaultAsync<DataApprovalRequestRecord>(
                    DataApprovalSql.FindRequestByIdempotency,
                    DataApprovalSqlParameters.Create(
                        ("TenantScopeKey", scope.TenantScopeKey),
                        ("IdempotencyKey", normalized.IdempotencyKey)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (replay is null)
            {
                return Result<DataApprovalRequestResponse>.Failure(new Error(
                    DataApprovalErrorCodes.RequestInvalid,
                    "The approval request could not be created.",
                    ErrorType.Conflict));
            }

            return await linkService.TryLinkWorkflowAsync(replay, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>人工重试 pending 请求的工作流关联。</summary>
    public async Task<Result<DataApprovalRequestResponse>> RetryLinkAsync(
        Guid requestId,
        RetryDataApprovalRequestBody request,
        CancellationToken cancellationToken = default)
    {
        if (request.Version < 1)
        {
            return Result<DataApprovalRequestResponse>.Failure(new Error(
                DataApprovalErrorCodes.RequestInvalid,
                "The approval request is invalid.",
                ErrorType.Validation));
        }

        var row = await FindAsync(requestId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return NotFound();
        }

        if (row.Version != request.Version)
        {
            return Result<DataApprovalRequestResponse>.Failure(new Error(
                DataApprovalErrorCodes.RecoveryRetryConflict,
                "The approval request changed concurrently.",
                ErrorType.Conflict));
        }

        return await linkService.TryLinkWorkflowAsync(row, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>人工重试批准后业务应用。</summary>
    public async Task<Result<DataApprovalRequestResponse>> RetryApplyAsync(
        Guid requestId,
        RetryDataApprovalRequestBody request,
        CancellationToken cancellationToken = default)
    {
        if (request.Version < 1)
        {
            return Result<DataApprovalRequestResponse>.Failure(new Error(
                DataApprovalErrorCodes.RequestInvalid,
                "The approval request is invalid.",
                ErrorType.Validation));
        }

        var row = await FindAsync(requestId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return NotFound();
        }

        if (row.Version != request.Version)
        {
            return Result<DataApprovalRequestResponse>.Failure(new Error(
                DataApprovalErrorCodes.ApplicationRetryConflict,
                "The approval request changed concurrently.",
                ErrorType.Conflict));
        }

        if (!DataApprovalApplicationRules.CanManualRetryApply(row.StatusKey, row.ApplicationStatusKey))
        {
            return Result<DataApprovalRequestResponse>.Failure(new Error(
                DataApprovalErrorCodes.ApplicationNotRetryable,
                "The approval request cannot retry application in the current state.",
                ErrorType.Conflict));
        }

        return await applicationService.TryApplyApprovedChangeAsync(
                row,
                row.SubmittedByUserId,
                $"{row.Id:D}:apply-retry",
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>取消待处理审批请求并联动工作流实例。</summary>
    public async Task<Result<DataApprovalRequestResponse>> CancelAsync(
        Guid requestId,
        Guid actorUserId,
        CancelDataApprovalRequestBody request,
        CancellationToken cancellationToken = default)
    {
        var idempotencyKey = request.IdempotencyKey?.Trim() ?? string.Empty;
        if (idempotencyKey.Length is < 1 or > 128)
        {
            return Result<DataApprovalRequestResponse>.Failure(new Error(
                DataApprovalErrorCodes.IdempotencyKeyInvalid,
                "The idempotency key is invalid.",
                ErrorType.Validation));
        }

        var scope = DataApprovalManagementScope.Resolve(currentTenant);
        var row = await FindAsync(requestId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return NotFound();
        }

        if (row.SubmittedByUserId != actorUserId)
        {
            return Result<DataApprovalRequestResponse>.Failure(new Error(
                DataApprovalErrorCodes.CancelForbidden,
                "Only the submitter can cancel this request.",
                ErrorType.Forbidden));
        }

        if (!DataApprovalStatusTransition.CanCancel(row.StatusKey))
        {
            return Result<DataApprovalRequestResponse>.Failure(new Error(
                DataApprovalErrorCodes.StatusInvalid,
                "The approval request cannot be cancelled in the current status.",
                ErrorType.Conflict));
        }

        if (row.WorkflowInstanceId is { } instanceId &&
            row.WorkflowRevision is { } revision)
        {
            var cancel = await workflowCanceller.CancelAsync(
                    actorUserId,
                    new CancelWorkflowInstanceCommand(
                        instanceId,
                        revision,
                        "data_approval.request.cancelled",
                        idempotencyKey),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!cancel.IsSuccess &&
                cancel.Error?.Code is not "workflow.instance.status_invalid" and
                    not "workflow.instance.not_found")
            {
                return Result<DataApprovalRequestResponse>.Failure(cancel.Error!);
            }
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                DataApprovalSql.UpdateStatus,
                DataApprovalSqlParameters.Create(
                    ("Id", requestId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("StatusKey", DataApprovalStatusKeys.Cancelled),
                    ("ResolvedAtUtc", now),
                    ("UpdatedAtUtc", now),
                    ("ExpectedStatusKey", row.StatusKey),
                    ("ExpectedVersion", row.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            var latest = await FindAsync(requestId, cancellationToken).ConfigureAwait(false);
            return latest is not null &&
                   string.Equals(latest.StatusKey, DataApprovalStatusKeys.Cancelled, StringComparison.Ordinal)
                ? Result<DataApprovalRequestResponse>.Success(Map(latest))
                : Result<DataApprovalRequestResponse>.Failure(new Error(
                    DataApprovalErrorCodes.StatusInvalid,
                    "The approval request could not be cancelled.",
                    ErrorType.Conflict));
        }

        var updated = await FindAsync(requestId, cancellationToken).ConfigureAwait(false);
        return updated is null
            ? NotFound()
            : Result<DataApprovalRequestResponse>.Success(Map(updated));
    }

    internal async Task<DataApprovalRequestRecord?> FindAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var scope = DataApprovalManagementScope.Resolve(currentTenant);
        return await queryExecutor.QuerySingleOrDefaultAsync<DataApprovalRequestRecord>(
                DataApprovalSql.FindRequestById,
                DataApprovalSqlParameters.Create(
                    ("Id", requestId),
                    ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool TryNormalizeCreate(
        CreateDataApprovalRequestBody request,
        out NormalizedCreate normalized,
        out Error? error)
    {
        normalized = default!;
        error = null;
        var scenarioKey = request.ScenarioKey?.Trim() ?? string.Empty;
        var proposedChangeJson = request.ProposedChangeJson?.Trim() ?? string.Empty;
        var idempotencyKey = request.IdempotencyKey?.Trim() ?? string.Empty;
        if (request.TargetEntityId == Guid.Empty ||
            scenarioKey.Length is < 1 or > 128 ||
            proposedChangeJson.Length is < 2 or > 65536 ||
            idempotencyKey.Length is < 1 or > 128)
        {
            error = new Error(
                DataApprovalErrorCodes.RequestInvalid,
                "The approval request is invalid.",
                ErrorType.Validation);
            return false;
        }

        if (!DataApprovalScenarioValidator.IsSupportedScenario(scenarioKey))
        {
            error = new Error(
                DataApprovalErrorCodes.ScenarioUnsupported,
                "The approval scenario is not supported.",
                ErrorType.Validation);
            return false;
        }

        normalized = new NormalizedCreate(
            scenarioKey,
            request.TargetEntityId,
            proposedChangeJson,
            idempotencyKey);
        return true;
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static bool RequiresSerialRuleBeforeSnapshot(string scenarioKey) =>
        string.Equals(scenarioKey, DataApprovalScenarioKeys.SerialRuleHostUpdate, StringComparison.Ordinal) ||
        string.Equals(scenarioKey, DataApprovalScenarioKeys.SerialRuleHostDisable, StringComparison.Ordinal);

    internal static DataApprovalRequestResponse Map(DataApprovalRequestRecord row) =>
        new(
            row.Id,
            row.ScenarioKey,
            row.TargetEntityId,
            row.StatusKey,
            row.BeforeSnapshotJson,
            row.AfterSnapshotJson,
            row.WorkflowInstanceId,
            row.WorkflowRevision,
            row.WorkflowDefinitionVersionId,
            row.SubmittedByUserId,
            row.SubmittedAtUtc,
            row.ResolvedAtUtc,
            row.RecoveryStatusKey,
            row.LastFailureCode,
            row.LastFailureMessage,
            row.LastRecoveryAttemptAtUtc,
            row.RecoveryAttemptCount,
            row.ApplicationStatusKey,
            row.LastApplicationFailureCode,
            row.LastApplicationFailureMessage,
            row.LastApplicationAttemptAtUtc,
            row.ApplicationAttemptCount,
            row.Version);

    private static Result<DataApprovalRequestResponse> NotFound() =>
        Result<DataApprovalRequestResponse>.Failure(new Error(
            DataApprovalErrorCodes.RequestNotFound,
            "The approval request was not found.",
            ErrorType.NotFound));

    private sealed record NormalizedCreate(
        string ScenarioKey,
        Guid TargetEntityId,
        string ProposedChangeJson,
        string IdempotencyKey);
}
