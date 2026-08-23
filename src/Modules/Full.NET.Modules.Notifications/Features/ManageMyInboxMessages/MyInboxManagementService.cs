using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Persistence;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Notifications.Features.ManageMyInboxMessages;

/// <summary>当前用户站内信已读状态变更。</summary>
internal sealed class MyInboxManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IOutboxWriter outboxWriter,
    NotificationRealtimeDelivery realtimeDelivery,
    IClock clock,
    ILogger<MyInboxManagementService> logger)
{
    /// <summary>
    /// 将当前用户的一条未读站内信标记为已读，并在事务内追加已读状态变更 Outbox 事件。
    /// </summary>
    /// <remarks>
    /// 站内信按收件人隔离，<paramref name="recipientUserId"/> 必须来自可信认证上下文；
    /// 重复标记已读幂等（已读状态直接返回当前值，不重复写 Outbox）。事务提交后再尝试
    /// 低延迟刷新未读数，失败仅告警，最终一致性由 Outbox 消费者保证。
    /// </remarks>
    public async Task<Result<InboxMessageResponse>> MarkReadAsync(
        Guid recipientUserId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var result = await transaction.ExecuteAsync(
                token => MarkReadCoreAsync(recipientUserId, messageId, token),
                cancellationToken)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await TryPublishUnreadCountAsync(recipientUserId, CancellationToken.None)
                .ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// 将当前用户所有未读站内信批量标记为已读，并在事务内追加已读状态变更 Outbox 事件。
    /// </summary>
    /// <remarks>
    /// 更新以 <c>RecipientUserId AND TenantId IS NULL AND Status = Unread</c> 为行守卫，
    /// 仅影响未读行；影响行数大于 0 时才写 Outbox，避免空操作产生无意义事件。
    /// 返回的未读数固定为 0，反映事务提交后的权威状态。
    /// </remarks>
    public async Task<Result<InboxUnreadCountResponse>> MarkAllReadAsync(
        Guid recipientUserId,
        CancellationToken cancellationToken = default)
    {
        var result = await transaction.ExecuteAsync(
                token => MarkAllReadCoreAsync(recipientUserId, token),
                cancellationToken)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await TryPublishUnreadCountAsync(recipientUserId, CancellationToken.None)
                .ConfigureAwait(false);
        }

        return result;
    }

    private async Task<Result<InboxMessageResponse>> MarkReadCoreAsync(
        Guid recipientUserId,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<InboxMessageRecord>(
                InboxMessageSql.FindForRecipientById,
                new { Id = messageId, RecipientUserId = recipientUserId },
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
                new
                {
                    Id = messageId,
                    RecipientUserId = recipientUserId,
                    ReadStatus = InboxMessageStatuses.Read,
                    UnreadStatus = InboxMessageStatuses.Unread,
                    ReadAtUtc = now,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affected > 0)
        {
            await AddReadStateChangedAsync(
                    recipientUserId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var updated = await queryExecutor.QuerySingleOrDefaultAsync<InboxMessageRecord>(
                InboxMessageSql.FindForRecipientById,
                new { Id = messageId, RecipientUserId = recipientUserId },
                cancellationToken)
            .ConfigureAwait(false);
        return Result<InboxMessageResponse>.Success(MyInboxQueryService.Map(updated!));
    }

    private async Task<Result<InboxUnreadCountResponse>> MarkAllReadCoreAsync(
        Guid recipientUserId,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                InboxMessageSql.MarkAllRead,
                new
                {
                    RecipientUserId = recipientUserId,
                    ReadStatus = InboxMessageStatuses.Read,
                    UnreadStatus = InboxMessageStatuses.Unread,
                    ReadAtUtc = now,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affected > 0)
        {
            await AddReadStateChangedAsync(
                    recipientUserId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<InboxUnreadCountResponse>.Success(new InboxUnreadCountResponse(0));
    }

    private async Task TryPublishUnreadCountAsync(
        Guid recipientUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await realtimeDelivery.PublishInboxUnreadCountAsync(
                    recipientUserId,
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
        CancellationToken cancellationToken) =>
        outboxWriter.AddAsync(
            NotificationRealtimeEventTypes.InboxReadStateChanged,
            1,
            new InboxReadStateChangedIntegrationEvent(recipientUserId),
            cancellationToken);

    private static Result<InboxMessageResponse> NotFound() =>
        Result<InboxMessageResponse>.Failure(new Error(
            NotificationsErrorCodes.InboxMessageNotFound,
            "The inbox message was not found.",
            ErrorType.NotFound));
}
