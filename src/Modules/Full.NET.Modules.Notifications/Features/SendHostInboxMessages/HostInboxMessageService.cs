using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Features.ManageMyInboxMessages;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Realtime;

namespace Full.NET.Modules.Notifications.Features.SendHostInboxMessages;

/// <summary>Host 管理员向指定用户发送站内信。</summary>
internal sealed class HostInboxMessageService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IRealtimePublisher realtimePublisher,
    IClock clock,
    IIdGenerator idGenerator)
{
    public Task<Result<InboxMessageResponse>> SendAsync(
        Guid actorUserId,
        SendHostInboxMessageRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => SendCoreAsync(actorUserId, request, token),
            cancellationToken);

    private async Task<Result<InboxMessageResponse>> SendCoreAsync(
        Guid actorUserId,
        SendHostInboxMessageRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateContent(request.Title, request.Content);
        if (validation is not null)
        {
            return validation;
        }

        var recipientExists = await queryExecutor.QuerySingleOrDefaultAsync<int?>(
                InboxMessageSql.HostRecipientExists,
                new { request.RecipientUserId },
                cancellationToken)
            .ConfigureAwait(false);
        if (recipientExists is null)
        {
            return RecipientNotFound();
        }

        var now = clock.UtcNow;
        var messageId = idGenerator.NewId();
        await commandExecutor.ExecuteAsync(
                InboxMessageSql.Insert,
                new
                {
                    Id = messageId,
                    request.RecipientUserId,
                    Title = request.Title.Trim(),
                    Content = request.Content.Trim(),
                    Status = InboxMessageStatuses.Unread,
                    CreatedAtUtc = now,
                    CreatedByUserId = actorUserId,
                },
                cancellationToken)
            .ConfigureAwait(false);

        var record = await queryExecutor.QuerySingleOrDefaultAsync<InboxMessageRecord>(
                InboxMessageSql.FindForRecipientById,
                new { Id = messageId, RecipientUserId = request.RecipientUserId },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        var response = MyInboxQueryService.Map(record);
        await realtimePublisher.PublishToUserAsync(
                request.RecipientUserId,
                new RealtimeMessage(
                    RealtimeMessageCodes.InboxMessageReceived,
                    new Dictionary<string, object?>
                    {
                        ["messageId"] = messageId,
                        ["title"] = response.Title,
                    }),
                cancellationToken)
            .ConfigureAwait(false);

        var unreadCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                InboxMessageSql.CountUnreadForRecipient,
                new
                {
                    RecipientUserId = request.RecipientUserId,
                    UnreadStatus = InboxMessageStatuses.Unread,
                },
                cancellationToken)
            .ConfigureAwait(false);
        await realtimePublisher.PublishToUserAsync(
                request.RecipientUserId,
                new RealtimeMessage(
                    RealtimeMessageCodes.InboxUnreadCountChanged,
                    new Dictionary<string, object?>
                    {
                        ["unreadCount"] = unreadCount,
                    }),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<InboxMessageResponse>.Success(response);
    }

    private static Result<InboxMessageResponse>? ValidateContent(string title, string content)
    {
        var normalizedTitle = title?.Trim() ?? string.Empty;
        var normalizedContent = content?.Trim() ?? string.Empty;
        if (normalizedTitle.Length is < 1 or > 200)
        {
            return ValidationFailure("Inbox message title must be between 1 and 200 characters.");
        }

        if (normalizedContent.Length is < 1 or > 4000)
        {
            return ValidationFailure("Inbox message content must be between 1 and 4000 characters.");
        }

        return null;
    }

    private static Result<InboxMessageResponse> ValidationFailure(string message) =>
        Result<InboxMessageResponse>.Failure(new Error(
            NotificationsErrorCodes.InboxValidationFailed,
            message,
            ErrorType.Validation));

    private static Result<InboxMessageResponse> RecipientNotFound() =>
        Result<InboxMessageResponse>.Failure(new Error(
            NotificationsErrorCodes.InboxRecipientNotFound,
            "The recipient user was not found.",
            ErrorType.NotFound));

    private static Result<InboxMessageResponse> NotFound() =>
        Result<InboxMessageResponse>.Failure(new Error(
            NotificationsErrorCodes.InboxMessageNotFound,
            "The inbox message was not found.",
            ErrorType.NotFound));
}
