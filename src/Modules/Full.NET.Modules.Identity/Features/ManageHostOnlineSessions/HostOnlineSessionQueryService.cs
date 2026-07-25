using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.ManageHostOnlineSessions;

/// <summary>Host 在线会话分页列表只读查询。</summary>
internal sealed class HostOnlineSessionQueryService(
    IQueryExecutor queryExecutor,
    IClock clock,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<HostOnlineSessionResponse>>> ListAsync(
        int page,
        int pageSize,
        string? usernameContains,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var filter = new
        {
            NowUtc = clock.UtcNow,
            UsernameContains = NormalizeUsernameFilter(usernameContains),
            Offset = offset,
            PageSize = pageSize,
        };
        var (countStatement, listStatement) = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => (
                OnlineSessionSql.CountActiveHostSessionsSqlServer,
                OnlineSessionSql.ListActiveHostSessionsSqlServer),
            DatabaseProvider.MySql => (
                OnlineSessionSql.CountActiveHostSessionsMySql,
                OnlineSessionSql.ListActiveHostSessionsMySql),
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                countStatement,
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<OnlineSessionListRow>(
                listStatement,
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<PagedResult<HostOnlineSessionResponse>>.Success(
            new PagedResult<HostOnlineSessionResponse>(items, page, pageSize, total));
    }

    private static HostOnlineSessionResponse Map(OnlineSessionListRow row) =>
        new(
            row.SessionId,
            row.UserId,
            row.Username,
            row.DisplayName,
            row.ClientId,
            row.ActiveTenantId,
            row.CreatedAtUtc,
            row.ExpiresAtUtc);

    private static string? NormalizeUsernameFilter(string? usernameContains)
    {
        var normalized = usernameContains?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
