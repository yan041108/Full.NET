using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Features.ManageMyInboxMessages;
using Full.NET.Modules.Notifications.Persistence;

namespace Full.NET.Modules.Notifications.Features.ProjectInboxFromIntent;

/// <summary>将已受理 Intent 幂等投影为当前作用域 Inbox；重复 Intent+收件人不得新增行。</summary>
internal sealed class InboxIntentProjectionService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IHostUserDirectory hostUserDirectory,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator)
{
    public async Task<Result<InboxMessageResponse>> ProjectAsync(
        Guid actorUserId,
        Guid intentId,
        Guid recipientUserId,
        string title,
        string content,
        CancellationToken cancellationToken = default)
    {
        var recipient = await hostUserDirectory.FindActiveHostUserAsync(
                recipientUserId,
                cancellationToken)
            .ConfigureAwait(false);
        if (recipient is null)
        {
            return Result<InboxMessageResponse>.Failure(new Error(
                NotificationsErrorCodes.InboxRecipientNotFound,
                "The recipient user was not found.",
                ErrorType.NotFound));
        }

        var scope = NotificationInboxScope.Resolve(currentTenant);
        return await transaction.ExecuteResultAsync(
                token => ProjectInAmbientTransactionAsync(
                    scope,
                    actorUserId,
                    intentId,
                    recipientUserId,
                    title,
                    content,
                    token),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 在调用方已开启的命令事务内投影 Inbox；目录解析必须已在事务外完成。
    /// </summary>
    public Task<Result<InboxMessageResponse>> ProjectInAmbientTransactionAsync(
        NotificationInboxScope scope,
        Guid actorUserId,
        Guid intentId,
        Guid recipientUserId,
        string title,
        string content,
        CancellationToken cancellationToken) =>
        ProjectCoreAsync(scope, actorUserId, intentId, recipientUserId, title, content, cancellationToken);

    private async Task<Result<InboxMessageResponse>> ProjectCoreAsync(
        NotificationInboxScope scope,
        Guid actorUserId,
        Guid intentId,
        Guid recipientUserId,
        string title,
        string content,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<InboxMessageRecord>(
                InboxMessageSql.FindByIntentRecipient,
                NotificationPlatformSqlParameters.Create(
                    ("IntentId", intentId),
                    ("RecipientUserId", recipientUserId),
                    ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return Result<InboxMessageResponse>.Success(MyInboxQueryService.Map(existing));
        }

        var insert = scope.IsHost
            ? InboxMessageSql.InsertHostForIntent
            : InboxMessageSql.InsertTenantForIntent;
        var now = clock.UtcNow;
        var messageId = idGenerator.NewId();
        var parameters = NotificationPlatformSqlParameters.Create(
            ("Id", messageId),
            ("RecipientUserId", recipientUserId),
            ("TenantScopeKey", scope.TenantScopeKey),
            ("IntentId", intentId),
            ("Title", title.Trim()),
            ("Content", content.Trim()),
            ("Status", InboxMessageStatuses.Unread),
            ("CreatedAtUtc", now),
            ("CreatedByUserId", actorUserId));
        await commandExecutor.ExecuteAsync(insert, parameters, cancellationToken).ConfigureAwait(false);

        var record = await queryExecutor.QuerySingleOrDefaultAsync<InboxMessageRecord>(
                InboxMessageSql.FindByIntentRecipient,
                NotificationPlatformSqlParameters.Create(
                    ("IntentId", intentId),
                    ("RecipientUserId", recipientUserId),
                    ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? Result<InboxMessageResponse>.Failure(new Error(
                NotificationsErrorCodes.InboxMessageNotFound,
                "The inbox message was not found.",
                ErrorType.NotFound))
            : Result<InboxMessageResponse>.Success(MyInboxQueryService.Map(record));
    }
}
