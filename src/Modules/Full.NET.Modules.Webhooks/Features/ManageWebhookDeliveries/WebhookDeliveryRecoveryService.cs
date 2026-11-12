using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Webhooks.Contracts;
using Full.NET.Modules.Webhooks.Features.ManageWebhookDeliveries.Persistence;
using Full.NET.Modules.Webhooks.Features.ManageWebhookSubscriptions;

namespace Full.NET.Modules.Webhooks.Features.ManageWebhookDeliveries;

/// <summary>操作员凭接收方证据关闭恢复后仍待发的原投递，不创建发送尝试。</summary>
internal sealed class WebhookDeliveryRecoveryService(IQueryExecutor queries, ICommandExecutor commands,
    ICommandTransaction transaction, IDatabaseTransactionBoundSessionLock locks, ICurrentTenant tenant,
    ICurrentSessionAuthorization sessions, IIndependentCurrentSessionAuthorization independent,
    WebhookDeliveryHistoryQueryService history, IClock clock, IIdGenerator ids)
{
    private static readonly string[] Rights =
        [WebhookPermissions.SubscriptionsRead, WebhookPermissions.DeliveriesRead, WebhookPermissions.DeliveriesReconcile];

    internal async Task<Result<WebhookDeliveryRecoveryReceipt>> ConfirmAsync(Guid subscriptionId, Guid deliveryId,
        ConfirmWebhookDeliveryRequest request, CancellationToken token)
    {
        if (subscriptionId == Guid.Empty || deliveryId == Guid.Empty || request.OperationId == Guid.Empty
            || request.EventId == Guid.Empty || request.Version is <= 0 or int.MaxValue
            || !IsSha256(request.PayloadSha256) || !IsSha256(request.ReceiverReceiptSha256))
            return Result<WebhookDeliveryRecoveryReceipt>.Failure(new(ValidationErrorCodes.Failed,
                "Delivery identities, a bounded version and two SHA-256 digests are required.", ErrorType.Validation));

        request = request with { PayloadSha256 = request.PayloadSha256.ToUpperInvariant(),
            ReceiverReceiptSha256 = request.ReceiverReceiptSha256.ToUpperInvariant() };
        var actor = await sessions.AuthorizeAllAsync(Rights, token).ConfigureAwait(false);
        if (actor?.TenantId is not { } tenantId || !IsCurrentTenant(tenantId)) return Denied();

        var owned = await history.GetAsync(subscriptionId, deliveryId, token).ConfigureAwait(false);
        if (!owned.IsSuccess) return Result<WebhookDeliveryRecoveryReceipt>.Failure(owned.Error!);
        var existing = await FindAsync(request.OperationId, token).ConfigureAwait(false);
        if (existing is not null) return Match(existing, actor, subscriptionId, deliveryId, request);
        if (!independent.HasIndependentReadCapacity)
            throw new ServiceCapacityExceededException(ServiceCapacityFailureKind.Rejected, TimeSpan.FromMilliseconds(250));

        // 同一租户操作键串行化，不同键由投递版本和状态决定唯一写入方。
        var resource = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"webhooks.recovery:{tenantId:N}:{request.OperationId:N}")));
        await using var lease = await locks.TryAcquireAsync(resource, token).ConfigureAwait(false);
        if (lease is null) return Conflict();
        return await transaction.ExecuteResultAsync(async cancellation =>
        {
            await lease.EnsureHeldAsync(cancellation).ConfigureAwait(false);
            if (!IsCurrentTenant(tenantId)) return Denied();
            var previous = await FindAsync(request.OperationId, cancellation).ConfigureAwait(false);
            if (previous is not null) return Match(previous, actor, subscriptionId, deliveryId, request);

            var delivery = owned.Value!;
            if (delivery.StatusKey != "pending" || delivery.Version != request.Version || delivery.EventId != request.EventId)
                return Conflict();
            var values = WebhookSqlParameters.Create(("Id", ids.NewId()), ("OperationId", request.OperationId),
                ("ActorUserId", actor.UserId), ("ActorSessionId", actor.SessionId), ("SubscriptionId", subscriptionId),
                ("DeliveryId", deliveryId), ("EventId", request.EventId), ("PreviousVersion", request.Version),
                ("Version", request.Version + 1), ("AttemptCount", delivery.AttemptCount),
                ("PayloadSha256", request.PayloadSha256), ("ReceiverReceiptSha256", request.ReceiverReceiptSha256),
                ("ConfirmedAtUtc", clock.UtcNow), ("PendingStatus", WebhookDeliveryStatuses.Pending),
                ("DeliveredStatus", WebhookDeliveryStatuses.Delivered));
            if (await commands.ExecuteAsync(WebhookDeliveryRecoverySql.Confirm, values, cancellation).ConfigureAwait(false) != 1)
                return Conflict();
            if (await commands.ExecuteAsync(WebhookDeliveryRecoverySql.Insert, values, cancellation).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("The delivery reconciliation has no matching audit receipt.");

            // Identity 权威复核走独立连接，撤权和会话变化须回滚状态及回执。
            var confirmed = await independent.AuthorizeAllAsync(Rights, cancellation).ConfigureAwait(false);
            if (confirmed != actor || !IsCurrentTenant(tenantId)) return Denied();
            await lease.EnsureHeldAsync(cancellation).ConfigureAwait(false);
            var persisted = await FindAsync(request.OperationId, cancellation).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The delivery reconciliation receipt was not persisted.");
            return Result<WebhookDeliveryRecoveryReceipt>.Success(persisted.ToReceipt());
        }, token).ConfigureAwait(false);
    }

    private bool IsCurrentTenant(Guid tenantId) => tenant.Id == tenantId && !tenant.IsHost;

    private Task<WebhookDeliveryRecoveryRecord?> FindAsync(Guid operationId, CancellationToken token) =>
        queries.QuerySingleOrDefaultAsync<WebhookDeliveryRecoveryRecord>(WebhookDeliveryRecoverySql.Find,
            WebhookSqlParameters.Create(("OperationId", operationId)), token);

    private static bool IsSha256(string? digest) => digest is { Length: 64 } && digest.All(Uri.IsHexDigit);

    private static Result<WebhookDeliveryRecoveryReceipt> Match(WebhookDeliveryRecoveryRecord row, AuthorizedSessionActor actor,
        Guid subscriptionId, Guid deliveryId, ConfirmWebhookDeliveryRequest request) =>
        row.ActorUserId == actor.UserId && row.TenantId == actor.TenantId && row.SubscriptionId == subscriptionId
        && row.DeliveryId == deliveryId && row.EventId == request.EventId && row.PreviousVersion == request.Version
        && row.PayloadSha256 == request.PayloadSha256 && row.ReceiverReceiptSha256 == request.ReceiverReceiptSha256
            ? Result<WebhookDeliveryRecoveryReceipt>.Success(row.ToReceipt()) : Conflict();

    private static Result<WebhookDeliveryRecoveryReceipt> Conflict() => Result<WebhookDeliveryRecoveryReceipt>.Failure(
        new(WebhookErrorCodes.RecoveryConflict, "The recovery operation, delivery state, version or evidence conflicts.", ErrorType.Conflict));

    private static Result<WebhookDeliveryRecoveryReceipt> Denied() => Result<WebhookDeliveryRecoveryReceipt>.Failure(
        new("authorization.forbidden", "The current tenant session and delivery reconciliation permissions are required.", ErrorType.Forbidden));
}
