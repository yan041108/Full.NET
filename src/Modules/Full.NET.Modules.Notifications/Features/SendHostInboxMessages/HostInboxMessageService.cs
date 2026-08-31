using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Features.ManageMyInboxMessages;
using Full.NET.Modules.Notifications.Persistence;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Notifications.Features.SendHostInboxMessages;

/// <summary>Host 管理员向指定用户发送站内信；禁止在租户会话中走该路径。</summary>
internal sealed class HostInboxMessageService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IOutboxWriter outboxWriter,
    IHostUserDirectory hostUserDirectory,
    ICurrentTenant currentTenant,
    NotificationRealtimeDelivery realtimeDelivery,
    IClock clock,
    IIdGenerator idGenerator,
    ILogger<HostInboxMessageService> logger)
{
    /// <summary>
    /// 向指定 Host 用户投递站内信：校验收件人存在后，在命令事务内写入站内信与实时修复 Outbox 事件。
    /// </summary>
    public async Task<Result<InboxMessageResponse>> SendAsync(
        Guid actorUserId,
        SendHostInboxMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!currentTenant.IsHost)
        {
            return ScopeForbidden();
        }

        var validation = ValidateContent(request.Title, request.Content);
        if (validation is not null)
        {
            return validation;
        }

        var recipient = await hostUserDirectory.FindActiveHostUserAsync(
                request.RecipientUserId,
                cancellationToken)
            .ConfigureAwait(false);
        if (recipient is null)
        {
            return RecipientNotFound();
        }

        var result = await transaction.ExecuteResultAsync(
                token => SendCoreAsync(actorUserId, request, token),
                cancellationToken)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await TryPublishMessageAsync(
                    request.RecipientUserId,
                    result.Value!,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        return result;
    }

    private async Task<Result<InboxMessageResponse>> SendCoreAsync(
        Guid actorUserId,
        SendHostInboxMessageRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var messageId = idGenerator.NewId();
        await commandExecutor.ExecuteAsync(
                InboxMessageSql.InsertHost,
                NotificationPlatformSqlParameters.Create(
                    ("Id", messageId),
                    ("RecipientUserId", request.RecipientUserId),
                    ("Title", request.Title.Trim()),
                    ("Content", request.Content.Trim()),
                    ("Status", InboxMessageStatuses.Unread),
                    ("CreatedAtUtc", now),
                    ("CreatedByUserId", actorUserId)),
                cancellationToken)
            .ConfigureAwait(false);

        var record = await queryExecutor.QuerySingleOrDefaultAsync<InboxMessageRecord>(
                InboxMessageSql.FindForRecipientById,
                NotificationPlatformSqlParameters.Create(
                    ("Id", messageId),
                    ("RecipientUserId", request.RecipientUserId),
                    ("TenantScopeKey", "host")),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        await outboxWriter.AddAsync(
                NotificationRealtimeEventTypes.InboxMessageReceived,
                1,
                new InboxMessageReceivedIntegrationEvent(
                    request.RecipientUserId,
                    messageId,
                    record.Title,
                    "host"),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<InboxMessageResponse>.Success(MyInboxQueryService.Map(record));
    }

    private async Task TryPublishMessageAsync(
        Guid recipientUserId,
        InboxMessageResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            await realtimeDelivery.PublishInboxMessageAsync(
                    new InboxMessageReceivedIntegrationEvent(
                        recipientUserId,
                        response.Id,
                        response.Title,
                        "host"),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to publish inbox message {MessageId} after the database commit.",
                response.Id);
        }
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

    private static Result<InboxMessageResponse> ScopeForbidden() =>
        Result<InboxMessageResponse>.Failure(new Error(
            NotificationsErrorCodes.InboxScopeForbidden,
            "Host inbox send requires the host session.",
            ErrorType.Forbidden));
}
