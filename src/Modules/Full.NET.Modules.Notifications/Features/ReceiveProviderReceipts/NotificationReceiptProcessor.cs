using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Notifications.Providers;

namespace Full.NET.Modules.Notifications.Features.ReceiveProviderReceipts;

/// <summary>先验签再去重并按状态机推进；原始 Body 不入库、不记日志。</summary>
internal sealed class NotificationReceiptProcessor(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IEnumerable<INotificationReceiptVerifier> verifiers,
    IClock clock,
    IIdGenerator idGenerator)
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
            return Result<NotificationReceiptAcceptedResponse>.Success(
                new NotificationReceiptAcceptedResponse(
                    existing.Id,
                    "duplicate",
                    existing.MappedStatusKey));
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

        var mapped = ParseStatus(payload.MappedStatusKey);
        var now = clock.UtcNow;
        var receiptId = idGenerator.NewId();
        var processStatus = "processed";
        if (delivery is not null)
        {
            var current = ParseStatus(delivery.StatusKey);
            var transition = NotificationDeliveryStateMachine.Apply(
                current,
                mapped,
                NotificationStatusSource.Receipt);
            if (transition.Applied)
            {
                await commandExecutor.ExecuteAsync(
                        NotificationPlatformSql.ApplyDeliveryStatus,
                        NotificationPlatformSqlParameters.Create(
                            ("Id", delivery.Id),
                            ("StatusKey", ToKey(transition.Status)),
                            ("Now", now),
                            ("Revision", delivery.Revision)),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                processStatus = transition.IsDuplicate ? "duplicate" : "ignored";
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
                    ("ProcessedAtUtc", now),
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
