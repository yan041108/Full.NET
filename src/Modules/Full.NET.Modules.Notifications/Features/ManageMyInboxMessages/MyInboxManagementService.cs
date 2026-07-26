using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Realtime;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Notifications.Features.ManageMyInboxMessages;

/// <summary>当前用户站内信已读状态变更。</summary>
internal sealed class MyInboxManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    MyInboxQueryService queries,
    IRealtimePublisher realtimePublisher,
    IClock clock,
    ILogger<MyInboxManagementService> logger)
{
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

        return Result<InboxUnreadCountResponse>.Success(new InboxUnreadCountResponse(0));
    }

    private async Task TryPublishUnreadCountAsync(
        Guid recipientUserId,
        CancellationToken cancellationToken)
    {
        try
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
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to publish unread count for user {UserId} after the database commit.",
                recipientUserId);
        }
    }

    private static Result<InboxMessageResponse> NotFound() =>
        Result<InboxMessageResponse>.Failure(new Error(
            NotificationsErrorCodes.InboxMessageNotFound,
            "The inbox message was not found.",
            ErrorType.NotFound));
}
