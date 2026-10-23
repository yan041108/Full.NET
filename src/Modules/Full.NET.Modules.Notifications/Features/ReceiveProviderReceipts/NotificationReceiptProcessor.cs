using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Execution;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Notifications.Providers;

namespace Full.NET.Modules.Notifications.Features.ReceiveProviderReceipts;

/// <summary>先验签再去重并按状态机推进；原始 Body 不入库、不记日志。</summary>
internal sealed class NotificationReceiptProcessor(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    INotificationProviderTypeCatalog catalog,
    IEnumerable<INotificationReceiptVerifier> verifiers,
    IClock clock,
    IIdGenerator idGenerator,
    NotificationQuotaGovernor quotaGovernor)
{
    public const int MaxBodyBytes = 32 * 1024;

    private readonly IReadOnlyDictionary<string, INotificationReceiptVerifier> _verifiers =
        verifiers.ToDictionary(item => item.ProviderTypeKey, StringComparer.Ordinal);

    public async Task<Result<NotificationReceiptAcceptedResponse>> ProcessAsync(
        string providerTypeKey,
        ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        if (body.Length > MaxBodyBytes)
        {
            return Result<NotificationReceiptAcceptedResponse>.Failure(new Error(
                NotificationsErrorCodes.ReceiptTooLarge,
                "The receipt payload exceeds the allowed size.",
                ErrorType.Validation));
        }

        if (catalog.TryGet(providerTypeKey, out var descriptor)
            && string.Equals(
                descriptor.ReceiptModeKey,
                NotificationReceiptModeKeys.None,
                StringComparison.Ordinal))
        {
            return Result<NotificationReceiptAcceptedResponse>.Failure(new Error(
                NotificationsErrorCodes.ReceiptNotSupported,
                "The provider type does not support trusted delivery or bounce receipts.",
                ErrorType.NotFound));
        }

        if (!_verifiers.TryGetValue(providerTypeKey, out var verifier))
        {
            return Result<NotificationReceiptAcceptedResponse>.Failure(new Error(
                NotificationsErrorCodes.ReceiptProviderUnknown,
                "The receipt provider type is not registered.",
                ErrorType.NotFound));
        }

        var verified = verifier.Verify(body, headers);
        if (!verified.IsSuccess)
        {
            return Result<NotificationReceiptAcceptedResponse>.Failure(verified.Error!);
        }

        var payload = verified.Value!;
        return await transaction.ExecuteResultAsync(
                token => ApplyAsync(providerTypeKey, payload, token),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<NotificationReceiptAcceptedResponse>> ApplyAsync(
        string providerTypeKey,
        VerifiedNotificationReceipt payload,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<NotificationReceiptRecord>(
                NotificationPlatformSql.FindReceiptByIdempotency,
                NotificationPlatformSqlParameters.Create(
                    ("ProviderTypeKey", providerTypeKey),
                    ("ReceiptIdempotencyKey", payload.ReceiptIdempotencyKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.ProcessStatusKey is not ("pending_association" or "pending_apply"))
            {
                return Result<NotificationReceiptAcceptedResponse>.Success(
                    new NotificationReceiptAcceptedResponse(
                        existing.Id,
                        "duplicate",
                        existing.MappedStatusKey));
            }

            return await TryApplyExistingAsync(providerTypeKey, payload, existing, cancellationToken)
                .ConfigureAwait(false);
        }

        NotificationDeliveryRecord? delivery = null;
        if (!string.IsNullOrEmpty(payload.ProviderMessageId))
        {
            var matched = await queryExecutor.QueryAsync<NotificationDeliveryRecord>(
                    NotificationPlatformSql.FindDeliveryByProviderMessageId,
                    NotificationPlatformSqlParameters.Create(
                        ("ProviderTypeKey", providerTypeKey),
                        ("ProviderMessageId", payload.ProviderMessageId)),
                    cancellationToken)
                .ConfigureAwait(false);
            delivery = matched.FirstOrDefault();
        }

        var now = clock.UtcNow;
        var receiptId = idGenerator.NewId();
        var processStatus = delivery is null ? "pending_association" : "pending_apply";
        DateTimeOffset? processedAt = null;
        if (delivery is not null)
        {
            processStatus = await ApplyReceiptToDeliveryAsync(delivery, payload.MappedStatusKey, now, cancellationToken)
                .ConfigureAwait(false);
            processedAt = processStatus is "pending_apply" ? null : now;
            if (processStatus == "completed" && !string.IsNullOrEmpty(payload.ProviderMessageId))
            {
                await quotaGovernor.ReconcileForMessageAsync(
                        delivery.Id, payload.ProviderMessageId, payload.MappedStatusKey, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var inserted = await commandExecutor.ExecuteAsync(
                NotificationPlatformSql.InsertReceipt,
                NotificationPlatformSqlParameters.Create(
                    ("Id", receiptId),
                    ("ProviderTypeKey", providerTypeKey),
                    ("ProviderMessageId", payload.ProviderMessageId),
                    ("ReceiptIdempotencyKey", payload.ReceiptIdempotencyKey),
                    ("DeliveryId", delivery?.Id),
                    ("ExternalStatusKey", payload.ExternalStatusKey),
                    ("MappedStatusKey", payload.MappedStatusKey),
                    ("PayloadDigest", payload.PayloadDigest),
                    ("ReceivedAtUtc", now),
                    ("ProcessedAtUtc", processedAt),
                    ("ProcessStatusKey", processStatus)),
                cancellationToken)
            .ConfigureAwait(false);
        if (inserted == 0)
        {
            var duplicate = await queryExecutor.QuerySingleOrDefaultAsync<NotificationReceiptRecord>(
                    NotificationPlatformSql.FindReceiptByIdempotency,
                    NotificationPlatformSqlParameters.Create(
                        ("ProviderTypeKey", providerTypeKey),
                        ("ReceiptIdempotencyKey", payload.ReceiptIdempotencyKey)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (duplicate is not null)
            {
                return Result<NotificationReceiptAcceptedResponse>.Success(
                    new NotificationReceiptAcceptedResponse(
                        duplicate.Id,
                        "duplicate",
                        duplicate.MappedStatusKey));
            }
        }

        return Result<NotificationReceiptAcceptedResponse>.Success(
            new NotificationReceiptAcceptedResponse(receiptId, processStatus, payload.MappedStatusKey));
    }

    private async Task<Result<NotificationReceiptAcceptedResponse>> TryApplyExistingAsync(
        string providerTypeKey,
        VerifiedNotificationReceipt payload,
        NotificationReceiptRecord receipt,
        CancellationToken cancellationToken)
    {
        NotificationDeliveryRecord? delivery = null;
        if (!string.IsNullOrEmpty(payload.ProviderMessageId))
        {
            var matched = await queryExecutor.QueryAsync<NotificationDeliveryRecord>(
                    NotificationPlatformSql.FindDeliveryByProviderMessageId,
                    NotificationPlatformSqlParameters.Create(
                        ("ProviderTypeKey", providerTypeKey),
                        ("ProviderMessageId", payload.ProviderMessageId)),
                    cancellationToken)
                .ConfigureAwait(false);
            delivery = matched.FirstOrDefault();
        }

        if (delivery is null)
        {
            return Result<NotificationReceiptAcceptedResponse>.Success(
                new NotificationReceiptAcceptedResponse(receipt.Id, receipt.ProcessStatusKey, receipt.MappedStatusKey));
        }

        var now = clock.UtcNow;
        var processStatus = await ApplyReceiptToDeliveryAsync(delivery, payload.MappedStatusKey, now, cancellationToken)
            .ConfigureAwait(false);
        if (processStatus == "completed" && !string.IsNullOrEmpty(payload.ProviderMessageId))
        {
            await quotaGovernor.ReconcileForMessageAsync(
                    delivery.Id, payload.ProviderMessageId, payload.MappedStatusKey, cancellationToken)
                .ConfigureAwait(false);
        }
        await commandExecutor.ExecuteAsync(
                NotificationPlatformSql.UpdateReceiptProcessing,
                NotificationPlatformSqlParameters.Create(
                    ("Id", receipt.Id),
                    ("DeliveryId", delivery.Id),
                    ("ProcessStatusKey", processStatus),
                    ("ProcessedAtUtc", processStatus is "pending_apply" ? null : now)),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<NotificationReceiptAcceptedResponse>.Success(
            new NotificationReceiptAcceptedResponse(receipt.Id, processStatus, receipt.MappedStatusKey));
    }

    /// <summary>
    /// 在 Attempt 提交后重新关联提前到达的回执，避免回执先到而永久停留在待关联。
    /// </summary>
    /// <param name="providerTypeKey">渠道类型键。</param>
    /// <param name="providerMessageId">提供程序消息号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本次实际推进到完成的回执条数。</returns>
    /// <remarks>
    /// 只处理同消息号且仍待关联或待应用的回执；
    /// 完成或已忽略的回执不会被再次推进，重复调用是幂等的。
    /// </remarks>
    public async Task<int> ReconcilePendingForMessageAsync(
        string providerTypeKey,
        string providerMessageId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(providerMessageId))
        {
            return 0;
        }

        return await transaction.ExecuteAsync(
                async token =>
                {
                    var pending = await queryExecutor.QueryAsync<NotificationReceiptRecord>(
                            NotificationPlatformSql.ListPendingReceiptsByProviderMessageId,
                            NotificationPlatformSqlParameters.Create(
                                ("ProviderTypeKey", providerTypeKey),
                                ("ProviderMessageId", providerMessageId)),
                            token)
                        .ConfigureAwait(false);
                    var completed = 0;
                    foreach (var receipt in pending)
                    {
                        // 已入库回执保存了验签时的映射结果，直接复用，不再解析原始 Body。
                        var payload = new VerifiedNotificationReceipt(
                            receipt.ReceiptIdempotencyKey,
                            receipt.ProviderMessageId,
                            receipt.ExternalStatusKey,
                            receipt.MappedStatusKey,
                            receipt.PayloadDigest);
                        var applied = await TryApplyExistingAsync(providerTypeKey, payload, receipt, token)
                            .ConfigureAwait(false);
                        if (applied.IsSuccess
                            && string.Equals(applied.Value!.ProcessStatusKey, "completed", StringComparison.Ordinal))
                        {
                            completed++;
                        }
                    }

                    return completed;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>每轮最多恢复五十条已具备 Attempt 的回执，不依赖厂商重复请求或仍有待发送任务。</summary>
    internal async Task RecoverPendingAsync(DatabaseProvider provider, CancellationToken cancellationToken)
    {
        var pending = await queryExecutor.QueryAsync<NotificationReceiptRecord>(provider == DatabaseProvider.MySql
            ? NotificationLifecycleSql.RecoverableReceiptsMySql : NotificationLifecycleSql.RecoverableReceiptsSqlServer,
            NotificationPlatformSqlParameters.Create(), cancellationToken).ConfigureAwait(false);
        foreach (var receipt in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payload = new VerifiedNotificationReceipt(receipt.ReceiptIdempotencyKey, receipt.ProviderMessageId,
                receipt.ExternalStatusKey, receipt.MappedStatusKey, receipt.PayloadDigest);
            await transaction.ExecuteResultAsync(token => TryApplyExistingAsync(receipt.ProviderTypeKey,
                payload, receipt, token), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string> ApplyReceiptToDeliveryAsync(
        NotificationDeliveryRecord delivery,
        string mappedStatusKey,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var mapped = ParseStatus(mappedStatusKey);
        var current = ParseStatus(delivery.StatusKey);
        var transition = NotificationDeliveryStateMachine.Apply(
            current,
            mapped,
            NotificationStatusSource.Receipt);
        if (!transition.Applied)
        {
            return transition.IsDuplicate ? "completed" : "ignored";
        }

        var affected = await commandExecutor.ExecuteAsync(
                NotificationPlatformSql.ApplyDeliveryStatus,
                NotificationPlatformSqlParameters.Create(
                    ("Id", delivery.Id),
                    ("StatusKey", ToKey(transition.Status)),
                    ("Now", now),
                    ("Revision", delivery.Revision)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected > 0)
        {
            return "completed";
        }

        var latest = await queryExecutor.QuerySingleOrDefaultAsync<NotificationDeliveryRecord>(
                NotificationPlatformSql.FindDeliveryById,
                NotificationPlatformSqlParameters.Create(("Id", delivery.Id)),
                cancellationToken)
            .ConfigureAwait(false);
        if (latest is null)
        {
            return "pending_apply";
        }

        var latestTransition = NotificationDeliveryStateMachine.Apply(
            ParseStatus(latest.StatusKey),
            mapped,
            NotificationStatusSource.Receipt);
        if (!latestTransition.Applied)
        {
            return latestTransition.IsDuplicate ? "completed" : "ignored";
        }

        affected = await commandExecutor.ExecuteAsync(
                NotificationPlatformSql.ApplyDeliveryStatus,
                NotificationPlatformSqlParameters.Create(
                    ("Id", latest.Id),
                    ("StatusKey", ToKey(latestTransition.Status)),
                    ("Now", now),
                    ("Revision", latest.Revision)),
                cancellationToken)
            .ConfigureAwait(false);
        return affected > 0 ? "completed" : "pending_apply";
    }

    private static NotificationDeliveryStatus ParseStatus(string statusKey) =>
        statusKey switch
        {
            "persisted" => NotificationDeliveryStatus.Persisted,
            "accepted" => NotificationDeliveryStatus.Accepted,
            "sent" => NotificationDeliveryStatus.Sent,
            "delivered" => NotificationDeliveryStatus.Delivered,
            "unknown" => NotificationDeliveryStatus.Unknown,
            "read" => NotificationDeliveryStatus.Read,
            "failed" => NotificationDeliveryStatus.Failed,
            "suppressed" => NotificationDeliveryStatus.Suppressed,
            "dead_lettered" => NotificationDeliveryStatus.DeadLettered,
            _ => NotificationDeliveryStatus.Unknown,
        };

    private static string ToKey(NotificationDeliveryStatus status) =>
        status switch
        {
            NotificationDeliveryStatus.Persisted => "persisted",
            NotificationDeliveryStatus.Accepted => "accepted",
            NotificationDeliveryStatus.Sent => "sent",
            NotificationDeliveryStatus.Delivered => "delivered",
            NotificationDeliveryStatus.Unknown => "unknown",
            NotificationDeliveryStatus.Read => "read",
            NotificationDeliveryStatus.Failed => "failed",
            NotificationDeliveryStatus.Suppressed => "suppressed",
            NotificationDeliveryStatus.DeadLettered => "dead_lettered",
            _ => "unknown",
        };
}
