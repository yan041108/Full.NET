using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Notifications.Features.ManageDeliveries;

/// <summary>当前作用域投递只读查询与人工重试；重试写 B0 审计且不排空其他在途任务。</summary>
internal sealed class NotificationDeliveryService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<NotificationDeliveryResponse>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                NotificationPlatformSql.CountDeliveriesForScope,
                NotificationPlatformSqlParameters.Create(("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider == DatabaseProvider.MySql
            ? NotificationPlatformSql.ListDeliveriesForScopeMySql
            : NotificationPlatformSql.ListDeliveriesForScopeSqlServer;
        var rows = await queryExecutor.QueryAsync<NotificationDeliveryRecord>(
                statement,
                NotificationPlatformSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("Offset", offset),
                    ("PageSize", pageSize)),
                cancellationToken)
            .ConfigureAwait(false);
        var items = new List<NotificationDeliveryResponse>(rows.Count);
        foreach (var row in rows)
        {
            items.Add(await MapAsync(row, includeReceipts: false, cancellationToken).ConfigureAwait(false));
        }

        return Result<PagedResult<NotificationDeliveryResponse>>.Success(
            new PagedResult<NotificationDeliveryResponse>(items, page, pageSize, total));
    }

    public async Task<Result<NotificationDeliveryResponse>> GetByIdAsync(
        Guid deliveryId,
        CancellationToken cancellationToken)
    {
        var record = await FindAsync(deliveryId, cancellationToken).ConfigureAwait(false);
        return record is null
            ? NotFound()
            : Result<NotificationDeliveryResponse>.Success(
                await MapAsync(record, includeReceipts: true, cancellationToken).ConfigureAwait(false));
    }

    public Task<Result<NotificationDeliveryResponse>> RetryAsync(
        Guid actorUserId,
        Guid deliveryId,
        RetryNotificationDeliveryRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteResultAsync(
            token => RetryCoreAsync(actorUserId, deliveryId, request, token),
            cancellationToken);

    private async Task<Result<NotificationDeliveryResponse>> RetryCoreAsync(
        Guid actorUserId,
        Guid deliveryId,
        RetryNotificationDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await FindAsync(deliveryId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 1 or > 128
            || reason.Contains("://", StringComparison.Ordinal)
            || reason.Any(char.IsControl))
        {
            return Result<NotificationDeliveryResponse>.Failure(new Error(
                NotificationsErrorCodes.DeliveryRetryInvalid,
                "The retry reason is invalid.",
                ErrorType.Validation));
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                NotificationPlatformSql.RetryDelivery,
                NotificationPlatformSqlParameters.Create(
                    ("Id", deliveryId),
                    ("NextAttemptAtUtc", now),
                    ("Now", now),
                    ("Revision", request.Revision)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return Result<NotificationDeliveryResponse>.Failure(new Error(
                NotificationsErrorCodes.DeliveryRetryConflict,
                "The delivery changed concurrently or cannot be retried.",
                ErrorType.Conflict));
        }

        var scope = NotificationInboxScope.Resolve(currentTenant);
        await commandExecutor.ExecuteAsync(
                scope.IsHost
                    ? NotificationPlatformSql.InsertDomainAuditHost
                    : NotificationPlatformSql.InsertDomainAuditTenant,
                NotificationPlatformSqlParameters.Create(
                    ("Id", idGenerator.NewId()),
                    ("OperationKey", "delivery.retry"),
                    ("ActorUserId", actorUserId),
                    ("ResourceTypeKey", "delivery"),
                    ("ResourceId", deliveryId),
                    ("OutcomeKey", "succeeded"),
                    ("DetailJson", "{}"),
                    ("CreatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        return await GetByIdAsync(deliveryId, cancellationToken).ConfigureAwait(false);
    }

    private Task<NotificationDeliveryRecord?> FindAsync(
        Guid deliveryId,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        return queryExecutor.QuerySingleOrDefaultAsync<NotificationDeliveryRecord>(
            NotificationPlatformSql.FindDeliveryForScope,
            NotificationPlatformSqlParameters.Create(
                ("Id", deliveryId),
                ("TenantScopeKey", scope.TenantScopeKey)),
            cancellationToken);
    }

    private async Task<NotificationDeliveryResponse> MapAsync(
        NotificationDeliveryRecord record,
        bool includeReceipts,
        CancellationToken cancellationToken)
    {
        var attempts = await queryExecutor.QueryAsync<NotificationDeliveryAttemptRecord>(
                NotificationPlatformSql.ListAttemptsByDelivery,
                NotificationPlatformSqlParameters.Create(("DeliveryId", record.Id)),
                cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<NotificationDeliveryReceiptResponse> receipts = [];
        if (includeReceipts)
        {
            var receiptRows = await queryExecutor.QueryAsync<NotificationReceiptRecord>(
                    NotificationPlatformSql.ListReceiptsByDelivery,
                    NotificationPlatformSqlParameters.Create(("DeliveryId", record.Id)),
                    cancellationToken)
                .ConfigureAwait(false);
            receipts = receiptRows.Select(item => new NotificationDeliveryReceiptResponse(
                    item.Id,
                    item.ProviderTypeKey,
                    item.ProviderMessageId,
                    item.ExternalStatusKey,
                    item.MappedStatusKey,
                    item.ProcessStatusKey,
                    item.ReceivedAtUtc,
                    item.ProcessedAtUtc))
                .ToArray();
        }

        return new NotificationDeliveryResponse(
            record.Id,
            record.IntentId,
            record.RecipientId,
            record.ChannelKey,
            record.ProviderProfileVersionId,
            record.BindingVersionId,
            record.StatusKey,
            record.Revision,
            record.NextAttemptAtUtc,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            attempts.Select(item => new NotificationDeliveryAttemptResponse(
                    item.Id,
                    item.AttemptNumber,
                    item.StatusKey,
                    item.ResultCategoryKey,
                    item.ProviderMessageId,
                    item.ErrorCode,
                    item.StartedAtUtc,
                    item.FinishedAtUtc))
                .ToArray(),
            receipts);
    }

    private static Result<NotificationDeliveryResponse> NotFound() =>
        Result<NotificationDeliveryResponse>.Failure(new Error(
            NotificationsErrorCodes.DeliveryNotFound,
            "The notification delivery was not found.",
            ErrorType.NotFound));
}
