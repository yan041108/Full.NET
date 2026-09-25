using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.QueryAuthenticationEvents;

/// <summary>在 Host 权限边界内查询已有认证审计，限制时间窗和单页数据量。</summary>
internal sealed class AuthenticationEventQueryService(
    IQueryExecutor queryExecutor,
    IClock clock,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<AuthenticationEventResponse>>> ListAsync(
        int page,
        int pageSize,
        Guid? userId,
        string? eventType,
        bool? succeeded,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        var end = toUtc ?? clock.UtcNow.AddMinutes(1);
        var start = fromUtc ?? end.AddDays(-1);
        // 暂以受限页号保护数据库；跨大时间窗的稳定游标翻页仍按 AE04 跟进。
        if (page is < 1 or > 500 || pageSize is < 1 or > 100
            || end <= start || end - start > TimeSpan.FromDays(31)
            || eventType is { Length: > 100 }
            || page > int.MaxValue / pageSize)
        {
            return Result<PagedResult<AuthenticationEventResponse>>.Failure(
                new Error("identity.authentication_events.invalid_query",
                    "Invalid authentication event query.", ErrorType.Validation));
        }

        var parameters = IdentitySqlParameters.Create(
            ("FromUtc", start), ("ToUtc", end), ("UserId", userId),
            ("EventType", string.IsNullOrWhiteSpace(eventType) ? null : eventType.Trim()),
            ("Succeeded", succeeded), ("Offset", (page - 1) * pageSize),
            ("PageSize", pageSize));
        var listSql = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => AuthenticationEventSql.ListSqlServer,
            DatabaseProvider.MySql => AuthenticationEventSql.ListMySql,
            _ => throw new InvalidOperationException("Unsupported database provider."),
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
            AuthenticationEventSql.Count, parameters, cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<AuthenticationEventResponse>(
            listSql, parameters, cancellationToken).ConfigureAwait(false);
        return Result<PagedResult<AuthenticationEventResponse>>.Success(
            new PagedResult<AuthenticationEventResponse>(rows.ToArray(), page, pageSize, total));
    }

    public async Task<AuthenticationEventResponse?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await queryExecutor.QuerySingleOrDefaultAsync<AuthenticationEventResponse>(
            AuthenticationEventSql.GetById,
            IdentitySqlParameters.Create(("Id", id)), cancellationToken).ConfigureAwait(false);
}
