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
using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Webhooks.Features.ManageWebhookDeliveries;

/// <summary>手动重放只追加一次排队意图，原事件身份和已完成尝试不变；不直接发送 HTTP。</summary>
internal sealed class WebhookDeliveryReplayService(IQueryExecutor queries, ICommandExecutor commands, ICommandTransaction transaction,
    IDatabaseTransactionBoundSessionLock locks, ICurrentTenant tenant, ICurrentSessionAuthorization sessions,
    IIndependentCurrentSessionAuthorization independent, WebhookDeliveryHistoryQueryService history, IClock clock, IIdGenerator ids)
{
    private static readonly string[] ReadRights = [WebhookPermissions.SubscriptionsRead, WebhookPermissions.DeliveriesRead];
    private static readonly string[] WriteRights = [WebhookPermissions.SubscriptionsRead, WebhookPermissions.DeliveriesRead, WebhookPermissions.DeliveriesReplay];

    internal async Task<Result<WebhookDeliveryReplayReceipt>> ReadAsync(Guid subscriptionId, Guid deliveryId, Guid operationId, CancellationToken token)
    {
        var actor = await sessions.AuthorizeAllAsync(ReadRights, token).ConfigureAwait(false);
        if (actor?.TenantId is null || tenant.Id != actor.TenantId || tenant.IsHost) return Denied();
        var owned = await history.GetAsync(subscriptionId, deliveryId, token).ConfigureAwait(false);
        if (!owned.IsSuccess) return Result<WebhookDeliveryReplayReceipt>.Failure(owned.Error!);
        var row = await FindAsync(operationId, token).ConfigureAwait(false);
        return row is not null && row.ActorUserId == actor.UserId && row.SubscriptionId == subscriptionId && row.DeliveryId == deliveryId
            ? Result<WebhookDeliveryReplayReceipt>.Success(row.ToReceipt()) : Result<WebhookDeliveryReplayReceipt>.Failure(new(WebhookErrorCodes.ReplayReceiptNotFound, "The replay receipt was not found.", ErrorType.NotFound));
    }

    internal async Task<Result<WebhookDeliveryReplayReceipt>> QueueAsync(Guid subscriptionId, Guid deliveryId, ReplayWebhookDeliveryRequest request, CancellationToken token)
    {
        if (request.OperationId == Guid.Empty || subscriptionId == Guid.Empty || deliveryId == Guid.Empty || request.Version is <= 0 or >= int.MaxValue - 2)
            return Result<WebhookDeliveryReplayReceipt>.Failure(new(ValidationErrorCodes.Failed, "A replay identity and bounded original version are required.", ErrorType.Validation));
        var actor = await sessions.AuthorizeAllAsync(WriteRights, token).ConfigureAwait(false);
        if (actor?.TenantId is not { } tenantId || tenant.Id != tenantId || tenant.IsHost) return Denied();
        var owned = await history.GetAsync(subscriptionId, deliveryId, token).ConfigureAwait(false);
        if (!owned.IsSuccess) return Result<WebhookDeliveryReplayReceipt>.Failure(owned.Error!);
        var existing = await FindAsync(request.OperationId, token).ConfigureAwait(false);
        if (existing is not null) return Match(existing, actor, subscriptionId, deliveryId, request.Version);
        if (!independent.HasIndependentReadCapacity) throw new ServiceCapacityExceededException(ServiceCapacityFailureKind.Rejected, TimeSpan.FromMilliseconds(250));
        // 同操作键跨投递串行化；不同操作争同一投递仍由原版本 CAS 决定唯一排队者。
        // MySQL 会话锁资源最多 64 字符；哈希仍绑定企业和操作，不使用平台截断。
        var resource = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"webhooks.replay:{tenantId:N}:{request.OperationId:N}")));
        await using var lease = await locks.TryAcquireAsync(resource, token).ConfigureAwait(false);
        if (lease is null) return Conflict();
        return await transaction.ExecuteResultAsync(async cancellation =>
        {
            await lease.EnsureHeldAsync(cancellation).ConfigureAwait(false);
            if (tenant.Id != tenantId || tenant.IsHost) return Denied();
            var previous = await FindAsync(request.OperationId, cancellation).ConfigureAwait(false);
            if (previous is not null) return Match(previous, actor, subscriptionId, deliveryId, request.Version);
            var current = await history.GetAsync(subscriptionId, deliveryId, cancellation).ConfigureAwait(false);
            if (!current.IsSuccess) return Result<WebhookDeliveryReplayReceipt>.Failure(current.Error!);
            var delivery = current.Value!;
            if (delivery.StatusKey != "failed" || delivery.Version != request.Version || delivery.AttemptCount == int.MaxValue) return Conflict();
            var now = clock.UtcNow;
            var values = WebhookSqlParameters.Create(("Id", ids.NewId()), ("OperationId", request.OperationId), ("ActorUserId", actor.UserId), ("ActorSessionId", actor.SessionId),
                ("SubscriptionId", subscriptionId), ("DeliveryId", deliveryId), ("EventId", delivery.EventId), ("PreviousVersion", request.Version),
                ("Version", request.Version + 1), ("AttemptCount", delivery.AttemptCount), ("QueuedAtUtc", now));
            if (await commands.ExecuteAsync(WebhookDeliveryReplaySql.Queue, values, cancellation).ConfigureAwait(false) != 1) return Conflict();
            if (await commands.ExecuteAsync(WebhookDeliveryReplaySql.Insert, values, cancellation).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("The replay has no matching audit receipt.");
            // 权威复核使用独立连接，Identity 查询不能加入 Webhooks 写事务；任一撤权、换会话或容量失败均回滚。
            foreach (var permission in WriteRights)
            {
                var confirmed = await independent.AuthorizeAsync(permission, cancellation).ConfigureAwait(false);
                if (confirmed != actor) return Denied();
            }
            if (tenant.Id != tenantId || tenant.IsHost) return Denied();
            await lease.EnsureHeldAsync(cancellation).ConfigureAwait(false);
            var persisted = await FindAsync(request.OperationId, cancellation).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The replay receipt was not persisted.");
            return Result<WebhookDeliveryReplayReceipt>.Success(persisted.ToReceipt());
        }, token).ConfigureAwait(false);
    }

    private Task<WebhookDeliveryReplayRecord?> FindAsync(Guid operationId, CancellationToken token) =>
        queries.QuerySingleOrDefaultAsync<WebhookDeliveryReplayRecord>(WebhookDeliveryReplaySql.Find, WebhookSqlParameters.Create(("OperationId", operationId)), token);
    private static Result<WebhookDeliveryReplayReceipt> Match(WebhookDeliveryReplayRecord row, AuthorizedSessionActor actor, Guid subscriptionId, Guid deliveryId, int version) =>
        row.ActorUserId == actor.UserId && row.TenantId == actor.TenantId && row.SubscriptionId == subscriptionId && row.DeliveryId == deliveryId && row.PreviousVersion == version
            ? Result<WebhookDeliveryReplayReceipt>.Success(row.ToReceipt()) : Conflict();
    private static Result<WebhookDeliveryReplayReceipt> Conflict() => Result<WebhookDeliveryReplayReceipt>.Failure(new(WebhookErrorCodes.ReplayConflict, "The replay identity, original version or delivery state conflicts. Read the original receipt and delivery before confirming again.", ErrorType.Conflict));
    private static Result<WebhookDeliveryReplayReceipt> Denied() => Result<WebhookDeliveryReplayReceipt>.Failure(new("authorization.forbidden", "The original tenant session and replay permissions are required.", ErrorType.Forbidden));
}
