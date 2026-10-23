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
    /// <summary>只有获独立权限的人工证据可以解除 Unknown；同事务收敛额度与本地许可。</summary>
    public Task<Result<NotificationDeliveryResponse>> ReconcileAsync(Guid actor, Guid deliveryId,
        ReconcileNotificationDeliveryRequest request, Execution.NotificationQuotaGovernor governor,
        CancellationToken cancellationToken) => transaction.ExecuteResultAsync(async token =>
    {
        var evidence = request.EvidenceReference?.Trim() ?? string.Empty;
        if (request.OutcomeKey is not ("confirmed_accepted" or "confirmed_not_called")
            || evidence.Length is < 1 or > 128 || evidence.Any(char.IsControl))
            return Result<NotificationDeliveryResponse>.Failure(new Error(NotificationsErrorCodes.DeliveryRetryInvalid,
                "A controlled outcome and short evidence reference are required.", ErrorType.Validation));
        if (await FindAsync(deliveryId, token).ConfigureAwait(false) is null) return NotFound();
        var called = request.OutcomeKey == "confirmed_accepted";
        if (await commandExecutor.ExecuteAsync(NotificationLifecycleSql.Reconcile,
            NotificationPlatformSqlParameters.Create(("Id", deliveryId), ("Revision", request.Revision),
                ("StatusKey", called ? "sent" : "failed"), ("Now", clock.UtcNow)), token).ConfigureAwait(false) == 0)
            return Result<NotificationDeliveryResponse>.Failure(new Error(NotificationsErrorCodes.DeliveryRetryConflict,
                "The delivery changed or has an active lease.", ErrorType.Conflict));
        var parameters = NotificationPlatformSqlParameters.Create(("DeliveryId", deliveryId),
            // 确认未调用也不能复活可能已撤销的旧许可；需要用户再次明确同意。
            ("StateKey", called ? "consumed" : "revoked"));
        var quotas = await queryExecutor.QueryAsync<NotificationQuotaReservationRecord>(
            NotificationLifecycleSql.PendingQuotas, parameters, token).ConfigureAwait(false);
        await governor.ResolveRowsAsync(quotas, called ? NotificationQuotaReservationStates.Consumed
            : NotificationQuotaReservationStates.Released, token).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(NotificationLifecycleSql.ReconcileSubscription, parameters, token).ConfigureAwait(false);
        var scope = NotificationInboxScope.Resolve(currentTenant);
        await commandExecutor.ExecuteAsync(scope.IsHost ? NotificationPlatformSql.InsertDomainAuditHost : NotificationPlatformSql.InsertDomainAuditTenant,
            NotificationPlatformSqlParameters.Create(("Id", idGenerator.NewId()), ("OperationKey", "delivery.reconcile"),
                ("ActorUserId", actor), ("ResourceTypeKey", "delivery"), ("ResourceId", deliveryId), ("OutcomeKey", request.OutcomeKey),
                ("DetailJson", new System.Text.Json.Nodes.JsonObject { ["evidenceReference"] = evidence }.ToJsonString()),
                ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),
                ("CreatedAtUtc", clock.UtcNow)), token).ConfigureAwait(false);
        return await GetByIdAsync(deliveryId, token).ConfigureAwait(false);
    }, cancellationToken);

    public async Task<Result<IReadOnlyList<NotificationDeliverySummaryResponse>>> SummaryAsync(
        Providers.INotificationProviderTypeCatalog catalog, CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        var rows = await queryExecutor.QueryAsync<NotificationDeliverySummaryRecord>(
            databaseOptions.Value.Provider == DatabaseProvider.SqlServer ? NotificationLifecycleSql.SummarySqlServer : NotificationLifecycleSql.Summary,
            NotificationPlatformSqlParameters.Create(("TenantScopeKey", scope.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<NotificationDeliverySummaryResponse>>.Success(rows.Select(row =>
            new NotificationDeliverySummaryResponse(row.ChannelKey, row.ProviderTypeKey, row.StatusKey, row.Count,
                row.OldestCreatedAtUtc, catalog.TryGet(row.ProviderTypeKey, out var provider) ? provider.ReceiptModeKey : "none")).ToArray());
    }

    /// <summary>人工终止 Unknown 不等于证明未发送，保留未决配额及订阅许可供独立对账。</summary>
    public Task<Result<NotificationDeliveryResponse>> DeadLetterAsync(Guid actor, Guid deliveryId,
        RetryNotificationDeliveryRequest request, CancellationToken cancellationToken) => transaction.ExecuteResultAsync(async token =>
    {
        var row = await FindAsync(deliveryId, token).ConfigureAwait(false);
        if (row is null) return NotFound();
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 1 or > 128 || reason.Any(char.IsControl))
            return Result<NotificationDeliveryResponse>.Failure(new Error(NotificationsErrorCodes.DeliveryRetryInvalid,
                "A short disposition reason is required.", ErrorType.Validation));
        if (await commandExecutor.ExecuteAsync(NotificationLifecycleSql.DeadLetter,
            NotificationPlatformSqlParameters.Create(("Id", deliveryId), ("Revision", request.Revision), ("Now", clock.UtcNow)), token)
            .ConfigureAwait(false) == 0)
            return Result<NotificationDeliveryResponse>.Failure(new Error(NotificationsErrorCodes.DeliveryRetryConflict,
                "The delivery changed or has an active lease.", ErrorType.Conflict));
        var scope = NotificationInboxScope.Resolve(currentTenant);
        await commandExecutor.ExecuteAsync(scope.IsHost ? NotificationPlatformSql.InsertDomainAuditHost : NotificationPlatformSql.InsertDomainAuditTenant,
            NotificationPlatformSqlParameters.Create(("Id", idGenerator.NewId()), ("OperationKey", "delivery.dead_letter"),
                ("ActorUserId", actor), ("ResourceTypeKey", "delivery"), ("ResourceId", deliveryId), ("OutcomeKey", "stopped"),
                ("DetailJson", new System.Text.Json.Nodes.JsonObject { ["reason"] = reason }.ToJsonString()),
                ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),
                ("CreatedAtUtc", clock.UtcNow)), token).ConfigureAwait(false);
        return await GetByIdAsync(deliveryId, token).ConfigureAwait(false);
    }, cancellationToken);

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
            items.Add(await MapAsync(row, includeDetails: false, cancellationToken).ConfigureAwait(false));
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
                await MapAsync(record, includeDetails: true, cancellationToken).ConfigureAwait(false));
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

        // 人工重试不能替代结果对账，也不能重置仍然暂挂的额度或一次性许可。
        var anchor = NotificationPlatformSqlParameters.Create(("DeliveryId", deliveryId));
        if (existing.StatusKey == "unknown"
            || (await queryExecutor.QueryAsync<NotificationQuotaReservationRecord>(NotificationLifecycleSql.PendingQuotas, anchor, cancellationToken)
                .ConfigureAwait(false)).Count > 0
            || await queryExecutor.QuerySingleOrDefaultAsync<long>(NotificationSubscriptionGrantSql.CountUnresolved, anchor, cancellationToken)
                .ConfigureAwait(false) > 0)
            return Result<NotificationDeliveryResponse>.Failure(new Error(NotificationsErrorCodes.DeliveryRetryConflict,
                "Reconcile the unknown result before retrying.", ErrorType.Conflict));

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
                    ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),
                    ("Id", idGenerator.NewId()),
                    ("OperationKey", "delivery.retry"),
                    ("ActorUserId", actorUserId),
                    ("ResourceTypeKey", "delivery"),
                    ("ResourceId", deliveryId),
                    ("OutcomeKey", "succeeded"),
                    ("DetailJson", new System.Text.Json.Nodes.JsonObject { ["reason"] = reason }.ToJsonString()),
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
        bool includeDetails,
        CancellationToken cancellationToken)
    {
        // 列表只返回投递行；用户打开详情时才加载重试和回执历史。
        IReadOnlyList<NotificationDeliveryAttemptRecord> attempts = includeDetails
            ? await queryExecutor.QueryAsync<NotificationDeliveryAttemptRecord>(
                NotificationPlatformSql.ListAttemptsByDelivery,
                NotificationPlatformSqlParameters.Create(("DeliveryId", record.Id)), cancellationToken).ConfigureAwait(false)
            : [];
        IReadOnlyList<NotificationDeliveryReceiptResponse> receipts = [];
        if (includeDetails)
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
            receipts, record.PreferenceVersion, record.PolicyDecisionKey, record.SuppressionReasonCode);
    }

    private static Result<NotificationDeliveryResponse> NotFound() =>
        Result<NotificationDeliveryResponse>.Failure(new Error(
            NotificationsErrorCodes.DeliveryNotFound,
            "The notification delivery was not found.",
            ErrorType.NotFound));
}
