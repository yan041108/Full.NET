using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Notifications.Features.ManageMyInboxMessages;

/// <summary>当前用户站内信分页查询与未读计数。</summary>
internal sealed class MyInboxQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<InboxMessageResponse>>> ListAsync(
        Guid recipientUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;

        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                InboxMessageSql.CountForRecipient,
                new { RecipientUserId = recipientUserId },
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<InboxMessageRecord>(
                ResolveListStatement(),
                new
                {
                    RecipientUserId = recipientUserId,
                    Offset = offset,
                    PageSize = pageSize,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return Result<PagedResult<InboxMessageResponse>>.Success(
            new PagedResult<InboxMessageResponse>(
                rows.Select(Map).ToArray(),
                page,
                pageSize,
                total));
    }

    public async Task<Result<InboxUnreadCountResponse>> GetUnreadCountAsync(
        Guid recipientUserId,
        CancellationToken cancellationToken = default)
    {
        var unreadCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                InboxMessageSql.CountUnreadForRecipient,
                new
                {
                    RecipientUserId = recipientUserId,
                    UnreadStatus = InboxMessageStatuses.Unread,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return Result<InboxUnreadCountResponse>.Success(
            new InboxUnreadCountResponse((int)unreadCount));
    }

    internal static InboxMessageResponse Map(InboxMessageRecord record) =>
        new(
            record.Id,
            record.Title,
            record.Content,
            record.Status,
            record.ReadAtUtc,
            record.CreatedAtUtc,
            record.CreatedByUserId);

    private SqlStatement ResolveListStatement() =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => InboxMessageSql.ListForRecipientSqlServer,
            DatabaseProvider.MySql => InboxMessageSql.ListForRecipientMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'.")
        };
}
