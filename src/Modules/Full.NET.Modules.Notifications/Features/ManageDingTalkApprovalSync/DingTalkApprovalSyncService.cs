using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Configuration;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Features;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Notifications.Providers.DingTalk;
using Full.NET.Modules.Notifications.Providers.Smtp;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Notifications.Features.ManageDingTalkApprovalSync;

/// <summary>
/// 钉钉审批镜像同步服务；Full.NET Workflow 保持流程权威，本服务只维护可观测镜像与补偿出站。
/// </summary>
internal sealed class DingTalkApprovalSyncService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    INotificationSecretResolver secretResolver,
    DingTalkAccessTokenCache tokenCache,
    IDingTalkTransport transport,
    IOptions<DingTalkApprovalSyncOptions> options,
    IOptions<DatabaseOptions> databaseOptions)
{
    private readonly DingTalkApprovalSyncOptions _options = options.Value;

    public async Task<Result<PagedResult<DingTalkApprovalSyncResponse>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                DingTalkApprovalSyncSql.CountForScope,
                NotificationPlatformSqlParameters.Create(("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider == DatabaseProvider.MySql
            ? DingTalkApprovalSyncSql.ListForScopeMySql
            : DingTalkApprovalSyncSql.ListForScopeSqlServer;
        var rows = await queryExecutor.QueryAsync<DingTalkApprovalSyncRecord>(
                statement,
                NotificationPlatformSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("Offset", offset),
                    ("PageSize", pageSize)),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<DingTalkApprovalSyncResponse>>.Success(
            new PagedResult<DingTalkApprovalSyncResponse>(
                rows.Select(Map).ToArray(),
                page,
                pageSize,
                total));
    }

    public async Task<Result<DingTalkApprovalSyncResponse>> GetByIdAsync(
        Guid syncId,
        CancellationToken cancellationToken)
    {
        var record = await FindScopedAsync(syncId, cancellationToken).ConfigureAwait(false);
        return record is null
            ? NotFound()
            : Result<DingTalkApprovalSyncResponse>.Success(Map(record));
    }

    public Task<Result<DingTalkApprovalSyncResponse>> CreateAsync(
        Guid actorUserId,
        CreateDingTalkApprovalSyncRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteResultAsync(
            token => CreateCoreAsync(actorUserId, request, token),
            cancellationToken);

    public Task<Result<DingTalkApprovalSyncResponse>> RetryAsync(
        Guid syncId,
        CancellationToken cancellationToken) =>
        transaction.ExecuteResultAsync(
            token => RetryCoreAsync(syncId, token),
            cancellationToken);

    public Task<Result<DingTalkApprovalSyncCallbackAcceptedResponse>> ApplyCallbackAsync(
        VerifiedDingTalkApprovalSyncCallback callback,
        CancellationToken cancellationToken) =>
        transaction.ExecuteResultAsync(
            token => ApplyCallbackCoreAsync(callback, token),
            cancellationToken);

    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return 0;
        }

        var statement = databaseOptions.Value.Provider == DatabaseProvider.MySql
            ? DingTalkApprovalSyncSql.ListPollCandidatesMySql
            : DingTalkApprovalSyncSql.ListPollCandidates;
        var rows = await queryExecutor.QueryAsync<DingTalkApprovalSyncRecord>(
                statement,
                NotificationPlatformSqlParameters.Create(
                    ("PendingOutbound", DingTalkApprovalSyncStatusKeys.PendingOutbound),
                    ("OutboundFailed", DingTalkApprovalSyncStatusKeys.OutboundFailed),
                    ("Running", DingTalkApprovalSyncStatusKeys.Running),
                    ("BatchSize", 20)),
                cancellationToken)
            .ConfigureAwait(false);
        var processed = 0;
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await ProcessRecordAsync(row, cancellationToken).ConfigureAwait(false))
            {
                processed++;
            }
        }

        return processed;
    }

    private async Task<Result<DingTalkApprovalSyncResponse>> CreateCoreAsync(
        Guid actorUserId,
        CreateDingTalkApprovalSyncRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return Result<DingTalkApprovalSyncResponse>.Failure(Disabled());
        }

        if (!DingTalkNotificationProviderAdapter.IsValidDingTalkUserId(request.OriginatorUserId)
            || request.DeptId <= 0
            || string.IsNullOrWhiteSpace(request.Title)
            || request.Title.Length > 256
            || request.Summary is { Length: > 2000 })
        {
            return Result<DingTalkApprovalSyncResponse>.Failure(ValidationFailed());
        }

        var scope = NotificationInboxScope.Resolve(currentTenant);
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<DingTalkApprovalSyncRecord>(
                DingTalkApprovalSyncSql.FindByWorkflowInstance,
                NotificationPlatformSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("WorkflowInstanceId", request.WorkflowInstanceId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return Result<DingTalkApprovalSyncResponse>.Success(Map(existing));
        }

        var now = clock.UtcNow;
        var id = idGenerator.NewId();
        var idempotencyKey = $"{request.WorkflowInstanceId:N}";
        await commandExecutor.ExecuteAsync(
                DingTalkApprovalSyncSql.Insert,
                NotificationPlatformSqlParameters.Create(
                    ("Id", id),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("WorkflowInstanceId", request.WorkflowInstanceId),
                    ("IdempotencyKey", idempotencyKey),
                    ("DingTalkProcessInstanceId", null),
                    ("ProcessCode", _options.ProcessCode),
                    ("OriginatorUserId", request.OriginatorUserId.Trim()),
                    ("DeptId", request.DeptId),
                    ("Title", request.Title.Trim()),
                    ("Summary", request.Summary?.Trim()),
                    ("StatusKey", DingTalkApprovalSyncStatusKeys.PendingOutbound),
                    ("ExternalStatusKey", null),
                    ("ExternalResultKey", null),
                    ("LastErrorCode", null),
                    ("LastSyncedAtUtc", null),
                    ("CreatedAtUtc", now),
                    ("UpdatedAtUtc", null),
                    ("CreatedByUserId", actorUserId)),
                cancellationToken)
            .ConfigureAwait(false);
        var created = await FindScopedAsync(id, cancellationToken).ConfigureAwait(false);
        return created is null
            ? Result<DingTalkApprovalSyncResponse>.Failure(ValidationFailed())
            : Result<DingTalkApprovalSyncResponse>.Success(Map(created));
    }

    private async Task<Result<DingTalkApprovalSyncResponse>> RetryCoreAsync(
        Guid syncId,
        CancellationToken cancellationToken)
    {
        var record = await FindScopedAsync(syncId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        if (record.StatusKey is not (
            DingTalkApprovalSyncStatusKeys.OutboundFailed
            or DingTalkApprovalSyncStatusKeys.PendingOutbound))
        {
            return Result<DingTalkApprovalSyncResponse>.Failure(new Error(
                NotificationsErrorCodes.DeliveryRetryConflict,
                "The approval sync record cannot be retried in its current state.",
                ErrorType.Conflict));
        }

        await ProcessRecordAsync(record, cancellationToken).ConfigureAwait(false);
        var refreshed = await FindScopedAsync(syncId, cancellationToken).ConfigureAwait(false);
        return refreshed is null
            ? NotFound()
            : Result<DingTalkApprovalSyncResponse>.Success(Map(refreshed));
    }

    private async Task<Result<DingTalkApprovalSyncCallbackAcceptedResponse>> ApplyCallbackCoreAsync(
        VerifiedDingTalkApprovalSyncCallback callback,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<DingTalkApprovalSyncRecord>(
                DingTalkApprovalSyncSql.FindByProcessInstanceId,
                NotificationPlatformSqlParameters.Create(
                    ("DingTalkProcessInstanceId", callback.ProcessInstanceId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return Result<DingTalkApprovalSyncCallbackAcceptedResponse>.Failure(NotFound().Error!);
        }

        var mappedStatus = MapExternalStatus(callback.ExternalStatusKey);
        if (mappedStatus is null)
        {
            return Result<DingTalkApprovalSyncCallbackAcceptedResponse>.Failure(ValidationFailed());
        }

        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(
                DingTalkApprovalSyncSql.UpdateMirrorState,
                NotificationPlatformSqlParameters.Create(
                    ("Id", record.Id),
                    ("TenantScopeKey", record.TenantScopeKey),
                    ("DingTalkProcessInstanceId", record.DingTalkProcessInstanceId),
                    ("StatusKey", mappedStatus),
                    ("ExternalStatusKey", callback.ExternalStatusKey),
                    ("ExternalResultKey", callback.ExternalResultKey),
                    ("LastErrorCode", null),
                    ("LastSyncedAtUtc", now),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<DingTalkApprovalSyncCallbackAcceptedResponse>.Success(
            new DingTalkApprovalSyncCallbackAcceptedResponse(record.Id, mappedStatus));
    }

    private async Task<bool> ProcessRecordAsync(
        DingTalkApprovalSyncRecord record,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return false;
        }

        var now = clock.UtcNow;
        try
        {
            var appSecret = await secretResolver
                .ResolveAsync(Providers.DingTalk.DingTalkNotificationProviderAdapter.ProviderTypeKeyValue, _options.AppSecretReference, cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrEmpty(appSecret))
            {
                await MarkFailedAsync(record, "secret_missing", now, cancellationToken)
                    .ConfigureAwait(false);
                return false;
            }

            var accessToken = await tokenCache
                .GetOrRefreshAsync(_options.AppKey, appSecret, cancellationToken)
                .ConfigureAwait(false);
            if (record.StatusKey is DingTalkApprovalSyncStatusKeys.PendingOutbound
                or DingTalkApprovalSyncStatusKeys.OutboundFailed)
            {
                var processInstanceId = await transport.CreateProcessInstanceAsync(
                        accessToken,
                        new DingTalkCreateProcessInstanceCommand(
                            record.OriginatorUserId,
                            record.ProcessCode,
                            record.DeptId,
                            _options.AgentId,
                            record.Title,
                            record.Summary,
                            record.IdempotencyKey),
                        cancellationToken)
                    .ConfigureAwait(false);
                await commandExecutor.ExecuteAsync(
                        DingTalkApprovalSyncSql.UpdateMirrorState,
                        NotificationPlatformSqlParameters.Create(
                            ("Id", record.Id),
                            ("TenantScopeKey", record.TenantScopeKey),
                            ("DingTalkProcessInstanceId", processInstanceId),
                            ("StatusKey", DingTalkApprovalSyncStatusKeys.Running),
                            ("ExternalStatusKey", "RUNNING"),
                            ("ExternalResultKey", null),
                            ("LastErrorCode", null),
                            ("LastSyncedAtUtc", now),
                            ("UpdatedAtUtc", now)),
                        cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }

            if (string.IsNullOrWhiteSpace(record.DingTalkProcessInstanceId))
            {
                return false;
            }

            var snapshot = await transport
                .GetProcessInstanceAsync(accessToken, record.DingTalkProcessInstanceId, cancellationToken)
                .ConfigureAwait(false);
            var mappedStatus = MapExternalStatus(snapshot.Status);
            if (mappedStatus is null || mappedStatus == record.StatusKey)
            {
                return false;
            }

            await commandExecutor.ExecuteAsync(
                    DingTalkApprovalSyncSql.UpdateMirrorState,
                    NotificationPlatformSqlParameters.Create(
                        ("Id", record.Id),
                        ("TenantScopeKey", record.TenantScopeKey),
                        ("DingTalkProcessInstanceId", record.DingTalkProcessInstanceId),
                        ("StatusKey", mappedStatus),
                        ("ExternalStatusKey", snapshot.Status),
                        ("ExternalResultKey", snapshot.Result),
                        ("LastErrorCode", null),
                        ("LastSyncedAtUtc", now),
                        ("UpdatedAtUtc", now)),
                    cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (DingTalkTransportException)
        {
            await MarkFailedAsync(record, "transport_failed", now, cancellationToken)
                .ConfigureAwait(false);
            return false;
        }
    }

    private async Task MarkFailedAsync(
        DingTalkApprovalSyncRecord record,
        string errorCode,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var statusKey = record.DingTalkProcessInstanceId is null
            ? DingTalkApprovalSyncStatusKeys.OutboundFailed
            : record.StatusKey;
        await commandExecutor.ExecuteAsync(
                DingTalkApprovalSyncSql.UpdateMirrorState,
                NotificationPlatformSqlParameters.Create(
                    ("Id", record.Id),
                    ("TenantScopeKey", record.TenantScopeKey),
                    ("DingTalkProcessInstanceId", record.DingTalkProcessInstanceId),
                    ("StatusKey", statusKey),
                    ("ExternalStatusKey", record.ExternalStatusKey),
                    ("ExternalResultKey", record.ExternalResultKey),
                    ("LastErrorCode", errorCode),
                    ("LastSyncedAtUtc", now),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<DingTalkApprovalSyncRecord?> FindScopedAsync(
        Guid syncId,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        return await queryExecutor.QuerySingleOrDefaultAsync<DingTalkApprovalSyncRecord>(
                DingTalkApprovalSyncSql.FindById,
                NotificationPlatformSqlParameters.Create(
                    ("Id", syncId),
                    ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static string? MapExternalStatus(string externalStatus) =>
        externalStatus switch
        {
            "RUNNING" => DingTalkApprovalSyncStatusKeys.Running,
            "COMPLETED" => DingTalkApprovalSyncStatusKeys.Completed,
            "TERMINATED" => DingTalkApprovalSyncStatusKeys.Terminated,
            _ => null,
        };

    private static DingTalkApprovalSyncResponse Map(DingTalkApprovalSyncRecord record) =>
        new(
            record.Id,
            record.WorkflowInstanceId,
            record.IdempotencyKey,
            record.DingTalkProcessInstanceId,
            record.ProcessCode,
            record.OriginatorUserId,
            record.DeptId,
            record.Title,
            record.Summary,
            record.StatusKey,
            record.ExternalStatusKey,
            record.ExternalResultKey,
            record.LastErrorCode,
            record.LastSyncedAtUtc,
            record.CreatedAtUtc,
            record.UpdatedAtUtc);

    private static Result<DingTalkApprovalSyncResponse> NotFound() =>
        Result<DingTalkApprovalSyncResponse>.Failure(new Error(
            NotificationsErrorCodes.DeliveryNotFound,
            "The approval sync record was not found in the current scope.",
            ErrorType.NotFound));

    private static Error ValidationFailed() => new(
        NotificationsErrorCodes.RecipientEndpointValidationFailed,
        "The approval sync request is invalid.",
        ErrorType.Validation);

    private static Error Disabled() => new(
        NotificationsErrorCodes.ReceiptNotSupported,
        "DingTalk approval sync is not enabled.",
        ErrorType.Validation);
}
