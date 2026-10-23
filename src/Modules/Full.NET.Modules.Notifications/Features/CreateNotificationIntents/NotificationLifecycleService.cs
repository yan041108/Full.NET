using System.Text.Json.Nodes;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Notifications.Execution;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Notifications.Features.CreateNotificationIntents;

/// <summary>在通知模块本地事务中撤回业务意图；与 Worker 以意图行到投递行的顺序竞争。</summary>
internal sealed class NotificationLifecycleService(IQueryExecutor queries, ICommandExecutor commands,
    ICommandTransaction transaction, ICurrentTenant tenant, IClock clock, IIdGenerator ids,
    IOptions<DatabaseOptions> database, IOutboxWriter outbox, NotificationQuotaGovernor quotaGovernor)
{
    public Task<Result<NotificationRevocationResponse>> RevokeAsync(Guid actor, Guid intentId,
        RevokeNotificationIntentRequest request, CancellationToken cancellationToken) => transaction.ExecuteResultAsync(async token =>
    {
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 1 or > 128 || reason.Any(char.IsControl))
            return Result<NotificationRevocationResponse>.Failure(new Error(NotificationsErrorCodes.TemplateValidationFailed,
                "A short revocation reason is required.", ErrorType.Validation));
        var scope = NotificationInboxScope.Resolve(tenant);
        var parameters = NotificationPlatformSqlParameters.Create(("Id", intentId), ("IntentId", intentId),
            ("TenantScopeKey", scope.TenantScopeKey), ("Revision", request.Revision), ("Reason", reason), ("Now", clock.UtcNow));
        var found = await queries.QuerySingleOrDefaultAsync<Guid>(database.Value.Provider == DatabaseProvider.MySql
            ? NotificationLifecycleSql.LockIntentMySql : NotificationLifecycleSql.LockIntentSqlServer, parameters, token)
            .ConfigureAwait(false);
        if (found == Guid.Empty) return Result<NotificationRevocationResponse>.Failure(new Error(
            NotificationsErrorCodes.IntentNotFound, "The active notification intent was not found.", ErrorType.NotFound));
        if (await commands.ExecuteAsync(NotificationLifecycleSql.RevokeIntent, parameters, token).ConfigureAwait(false) == 0)
            return Result<NotificationRevocationResponse>.Failure(new Error(NotificationsErrorCodes.DeliveryRetryConflict,
                "The notification changed concurrently.", ErrorType.Conflict));
        var stopped = await commands.ExecuteAsync(NotificationLifecycleSql.StopDeliveries, parameters, token).ConfigureAwait(false);
        // 已确定本轮未跨外发边界时，撤回事务自行释放许可，不能依赖可能崩溃的 Worker 收尾。
        // 仅处理当前租约的 reserved；旧世代 Unknown 和跨边界占额仍须独立对账。
        var unusedQuotas = await queries.QueryAsync<NotificationQuotaReservationRecord>(
            NotificationLifecycleSql.RevokedUnusedQuotas, parameters, token).ConfigureAwait(false);
        await quotaGovernor.ResolveRowsAsync(unusedQuotas, NotificationQuotaReservationStates.Released, token).ConfigureAwait(false);
        await commands.ExecuteAsync(NotificationLifecycleSql.ReleaseRevokedUnusedSubscriptions, parameters, token).ConfigureAwait(false);
        var crossed = await queries.QuerySingleOrDefaultAsync<int>(NotificationLifecycleSql.CountCrossed, parameters, token).ConfigureAwait(false);
        var inbox = await commands.ExecuteAsync(NotificationLifecycleSql.RevokeInbox, parameters, token).ConfigureAwait(false);
        await commands.ExecuteAsync(scope.IsHost ? NotificationPlatformSql.InsertDomainAuditHost : NotificationPlatformSql.InsertDomainAuditTenant,
            NotificationPlatformSqlParameters.Create(("Id", ids.NewId()), ("OperationKey", "intent.revoke"),
                ("ActorUserId", actor), ("ResourceTypeKey", "intent"), ("ResourceId", intentId),
                ("OutcomeKey", crossed > 0 ? "partially_stopped" : "stopped"),
                ("DetailJson", new JsonObject { ["reason"] = reason, ["crossedSendBoundary"] = crossed }.ToJsonString()),
                ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),
                ("CreatedAtUtc", clock.UtcNow)), token).ConfigureAwait(false);
        if (inbox > 0)
        {
            var recipients = await queries.QueryAsync<NotificationRecipientRecord>(NotificationPlatformSql.ListRecipientsByIntent,
                parameters, token).ConfigureAwait(false);
            foreach (var userId in recipients.Where(r => r.UserId.HasValue).Select(r => r.UserId!.Value).Distinct())
                await outbox.AddAsync(NotificationRealtimeEventTypes.InboxReadStateChanged, 1,
                    new InboxReadStateChangedIntegrationEvent(userId, scope.TenantScopeKey), token).ConfigureAwait(false);
        }
        return Result<NotificationRevocationResponse>.Success(new(stopped, crossed, inbox));
    }, cancellationToken);
}
