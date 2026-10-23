using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Persistence;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Notifications.Features.ManageMyInboxMessages;

/// <summary>当前用户站内信已读状态变更；只影响受信会话作用域内的行。</summary>
internal sealed class MyInboxManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IOutboxWriter outboxWriter,
    ICurrentTenant currentTenant,
    NotificationRealtimeDelivery realtimeDelivery,
    IClock clock,
    ILogger<MyInboxManagementService> logger)
{
    /// <summary>幂等归档有界的本人消息集合，未命中的他人或跨租户标识不影响任何行。</summary>
    public async Task<Result<InboxUnreadCountResponse>> ArchiveAsync(Guid userId, IReadOnlyList<Guid>? messageIds,
        CancellationToken cancellationToken)
    {
        if (messageIds is null || messageIds.Count is < 1 or > 100 || messageIds.Any(id => id == Guid.Empty))
            return Result<InboxUnreadCountResponse>.Failure(new Error(NotificationsErrorCodes.TemplateValidationFailed,
                "The archive batch must contain 1 to 100 message identifiers.", ErrorType.Validation));
        var scope = NotificationInboxScope.Resolve(currentTenant);
        var result = await transaction.ExecuteAsync(async token =>
        {
            var changed = await commandExecutor.ExecuteAsync(NotificationLifecycleSql.Archive,
                NotificationPlatformSqlParameters.Create(("TenantScopeKey", scope.TenantScopeKey),
                    ("UserId", userId), ("Ids", messageIds.Distinct().ToArray()), ("Now", clock.UtcNow)), token)
                .ConfigureAwait(false);
            if (changed > 0) await AddReadStateChangedAsync(userId, scope.TenantScopeKey, token).ConfigureAwait(false);
            var unread = await queryExecutor.QuerySingleOrDefaultAsync<int>(InboxMessageSql.CountUnreadForRecipient,
                NotificationPlatformSqlParameters.Create(("RecipientUserId", userId),
                    ("TenantScopeKey", scope.TenantScopeKey), ("UnreadStatus", InboxMessageStatuses.Unread)), token)
                .ConfigureAwait(false);
            return Result<InboxUnreadCountResponse>.Success(new InboxUnreadCountResponse(unread));
        }, cancellationToken).ConfigureAwait(false);
        await TryPublishUnreadCountAsync(userId, scope.TenantScopeKey, CancellationToken.None).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// 将当前用户的一条未读站内信标记为已读，并在事务内追加已读状态变更 Outbox 事件。
    /// </summary>
    public async Task<Result<InboxMessageResponse>> MarkReadAsync(
        Guid recipientUserId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        var result = await transaction.ExecuteAsync(
                token => MarkReadCoreAsync(scope, recipientUserId, messageId, token),
                cancellationToken)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await TryPublishUnreadCountAsync(
                    recipientUserId,
                    scope.TenantScopeKey,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// 将当前用户当前作用域内所有未读站内信批量标记为已读。
    /// </summary>
    public async Task<Result<InboxUnreadCountResponse>> MarkAllReadAsync(
        Guid recipientUserId,
        CancellationToken cancellationToken = default)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        var result = await transaction.ExecuteAsync(
                token => MarkAllReadCoreAsync(scope, recipientUserId, token),
                cancellationToken)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await TryPublishUnreadCountAsync(
                    recipientUserId,
                    scope.TenantScopeKey,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        return result;
    }

    private async Task<Result<InboxMessageResponse>> MarkReadCoreAsync(
        NotificationInboxScope scope,
        Guid recipientUserId,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<InboxMessageRecord>(
                InboxMessageSql.FindForRecipientById,
                NotificationPlatformSqlParameters.Create(
                    ("Id", messageId),
                    ("RecipientUserId", recipientUserId),
                    ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        if (string.Equals(existing.Status, InboxMessageStatuses.Read, StringComparison.Ordinal))
        {
            return Result<InboxMessageResponse>.Success(MyInboxQueryService.Map(existing));
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                InboxMessageSql.MarkRead,
                NotificationPlatformSqlParameters.Create(
                    ("Id", messageId),
                    ("RecipientUserId", recipientUserId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("ReadStatus", InboxMessageStatuses.Read),
                    ("UnreadStatus", InboxMessageStatuses.Unread),
                    ("ReadAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected > 0)
        {
            await AddReadStateChangedAsync(
                    recipientUserId,
                    scope.TenantScopeKey,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var updated = await queryExecutor.QuerySingleOrDefaultAsync<InboxMessageRecord>(
                InboxMessageSql.FindForRecipientById,
                NotificationPlatformSqlParameters.Create(
                    ("Id", messageId),
                    ("RecipientUserId", recipientUserId),
                    ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<InboxMessageResponse>.Success(MyInboxQueryService.Map(updated!));
    }

    private async Task<Result<InboxUnreadCountResponse>> MarkAllReadCoreAsync(
        NotificationInboxScope scope,
        Guid recipientUserId,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                InboxMessageSql.MarkAllRead,
                NotificationPlatformSqlParameters.Create(
                    ("RecipientUserId", recipientUserId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("ReadStatus", InboxMessageStatuses.Read),
                    ("UnreadStatus", InboxMessageStatuses.Unread),
                    ("ReadAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected > 0)
        {
            await AddReadStateChangedAsync(
                    recipientUserId,
                    scope.TenantScopeKey,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<InboxUnreadCountResponse>.Success(new InboxUnreadCountResponse(0));
    }

    private async Task TryPublishUnreadCountAsync(
        Guid recipientUserId,
        string tenantScopeKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await realtimeDelivery.PublishInboxUnreadCountAsync(
                    recipientUserId,
                    tenantScopeKey,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to publish unread count for user {UserId} after the database commit.",
                recipientUserId);
        }
    }

    private Task AddReadStateChangedAsync(
        Guid recipientUserId,
        string tenantScopeKey,
        CancellationToken cancellationToken) =>
        outboxWriter.AddAsync(
            NotificationRealtimeEventTypes.InboxReadStateChanged,
            1,
            new InboxReadStateChangedIntegrationEvent(recipientUserId, tenantScopeKey),
            cancellationToken);

    private static Result<InboxMessageResponse> NotFound() =>
        Result<InboxMessageResponse>.Failure(new Error(
            NotificationsErrorCodes.InboxMessageNotFound,
            "The inbox message was not found.",
            ErrorType.NotFound));
}
