using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Realtime;

namespace Full.NET.Modules.Notifications.Features.ManageMyInboxMessages;

/// <summary>当前用户站内信已读状态变更。</summary>
internal sealed class MyInboxManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    MyInboxQueryService queries,
    IRealtimePublisher realtimePublisher,
    IClock clock)
{
    public Task<Result<InboxMessageResponse>> MarkReadAsync(
        Guid recipientUserId,
        Guid messageId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => MarkReadCoreAsync(recipientUserId, messageId, token),
            cancellationToken);

    public Task<Result<InboxUnreadCountResponse>> MarkAllReadAsync(
        Guid recipientUserId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => MarkAllReadCoreAsync(recipientUserId, token),
            cancellationToken);

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
        await commandExecutor.ExecuteAsync(
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

        await PublishUnreadCountAsync(recipientUserId, cancellationToken).ConfigureAwait(false);

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
        await commandExecutor.ExecuteAsync(
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

        return await PublishUnreadCountAsync(recipientUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<InboxUnreadCountResponse>> PublishUnreadCountAsync(
        Guid recipientUserId,
        CancellationToken cancellationToken)
    {
        var countResult = await queries.GetUnreadCountAsync(recipientUserId, cancellationToken)
            .ConfigureAwait(false);
        if (countResult.IsSuccess)
        {
            await realtimePublisher.PublishToUserAsync(
                    recipientUserId,
                    new RealtimeMessage(
                        RealtimeMessageCodes.InboxUnreadCountChanged,
                        new Dictionary<string, object?>
                        {
                            ["unreadCount"] = countResult.Value!.UnreadCount,
                        }),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return countResult;
    }

    private static Result<InboxMessageResponse> NotFound() =>
        Result<InboxMessageResponse>.Failure(new Error(
            NotificationsErrorCodes.InboxMessageNotFound,
            "The inbox message was not found.",
            ErrorType.NotFound));
}
