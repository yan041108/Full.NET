using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Notifications.Features.ManageMyInboxMessages;

/// <summary>当前用户站内信分页查询与未读计数；作用域只来自受信会话。</summary>
internal sealed class MyInboxQueryService(
    IQueryExecutor queryExecutor,
    ICurrentTenant currentTenant,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>
    /// 按收件人与当前作用域分页查询站内信，按创建时间倒序排列。
    /// </summary>
    /// <remarks>
    /// 查询以 <paramref name="recipientUserId"/> 与受信 <c>TenantScopeKey</c> 共同作为行守卫，
    /// 收件人标识必须来自可信认证上下文；分页参数在服务端做上下界钳制。
    /// </remarks>
    public async Task<Result<PagedResult<InboxMessageResponse>>> ListAsync(
        Guid recipientUserId,
        int page,
        int pageSize,
        InboxMessageListFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var scope = NotificationInboxScope.Resolve(currentTenant);
        var parameters = BuildFilterParameters(scope.TenantScopeKey, recipientUserId, filter, offset, pageSize);

        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                InboxMessageSql.CountForRecipient,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<InboxMessageRecord>(
                ResolveListStatement(),
                parameters,
                cancellationToken)
            .ConfigureAwait(false);

        return Result<PagedResult<InboxMessageResponse>>.Success(
            new PagedResult<InboxMessageResponse>(
                rows.Select(Map).ToArray(),
                page,
                pageSize,
                total));
    }

    /// <summary>
    /// 查询指定收件人在当前作用域的未读站内信数量，作为实时徽标的权威值。
    /// </summary>
    public async Task<Result<InboxUnreadCountResponse>> GetUnreadCountAsync(
        Guid recipientUserId,
        CancellationToken cancellationToken = default)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        var unreadCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                InboxMessageSql.CountUnreadForRecipient,
                NotificationPlatformSqlParameters.Create(
                    ("RecipientUserId", recipientUserId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("UnreadStatus", InboxMessageStatuses.Unread)),
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

    private static Dictionary<string, object?> BuildFilterParameters(
        string tenantScopeKey,
        Guid recipientUserId,
        InboxMessageListFilter? filter,
        int offset,
        int pageSize)
    {
        var title = string.IsNullOrWhiteSpace(filter?.Title) ? null : filter.Title.Trim();
        var status = NormalizeStatusFilter(filter?.Status);
        return NotificationPlatformSqlParameters.Create(
            ("RecipientUserId", recipientUserId),
            ("TenantScopeKey", tenantScopeKey),
            ("Offset", offset),
            ("PageSize", pageSize),
            ("Title", title),
            ("TitlePattern", title is null ? null : $"%{title}%"),
            ("Status", status));
    }

    internal static string? NormalizeStatusFilter(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        var normalized = status.Trim();
        return normalized is InboxMessageStatuses.Read or InboxMessageStatuses.Unread
            ? normalized
            : null;
    }
}
