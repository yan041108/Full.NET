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

namespace Full.NET.Modules.Notifications.Features.SendTenantInboxMessages;

/// <summary>当前租户管理员向指定用户发送站内信；目录解析在事务外完成。</summary>
internal sealed class TenantInboxMessageService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IOutboxWriter outboxWriter,
    IHostUserDirectory hostUserDirectory,
    ICurrentTenant currentTenant,
    NotificationRealtimeDelivery realtimeDelivery,
    IClock clock,
    IIdGenerator idGenerator,
    ILogger<TenantInboxMessageService> logger)
{
    public async Task<Result<InboxMessageResponse>> SendAsync(
        Guid actorUserId,
        SendTenantInboxMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (currentTenant.IsHost || !currentTenant.IsAvailable || currentTenant.Id is null)
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

        var scope = NotificationInboxScope.Resolve(currentTenant);
        var result = await transaction.ExecuteResultAsync(
                token => SendCoreAsync(scope, actorUserId, request, token),
                cancellationToken)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await TryPublishMessageAsync(
                    request.RecipientUserId,
                    scope.TenantScopeKey,
                    result.Value!,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        return result;
    }

    private async Task<Result<InboxMessageResponse>> SendCoreAsync(
        NotificationInboxScope scope,
        Guid actorUserId,
        SendTenantInboxMessageRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var messageId = idGenerator.NewId();
        await commandExecutor.ExecuteAsync(
                InboxMessageSql.InsertTenant,
                NotificationPlatformSqlParameters.Create(
                    ("Id", messageId),
                    ("RecipientUserId", request.RecipientUserId),
                    ("TenantScopeKey", scope.TenantScopeKey),
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
                    ("TenantScopeKey", scope.TenantScopeKey)),
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
                    scope.TenantScopeKey),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<InboxMessageResponse>.Success(MyInboxQueryService.Map(record));
    }

    private async Task TryPublishMessageAsync(
        Guid recipientUserId,
        string tenantScopeKey,
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
                        tenantScopeKey),
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
            "Tenant inbox send requires a tenant session.",
            ErrorType.Forbidden));
}
