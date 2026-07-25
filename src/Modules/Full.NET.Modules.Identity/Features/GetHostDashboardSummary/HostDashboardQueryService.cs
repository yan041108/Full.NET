using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.GetHostDashboardSummary;

/// <summary>聚合 Host 工作台指标；只读跨表查询，结果允许最终一致。</summary>
internal sealed class HostDashboardQueryService(
    IQueryExecutor queryExecutor,
    IClock clock,
    IOptions<DatabaseOptions> databaseOptions)
{
    private const int RecentActivityTake = 5;

    public async Task<Result<HostDashboardSummaryResponse>> GetSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var startOfDayUtc = new DateTimeOffset(
            now.UtcDateTime.Date,
            TimeSpan.Zero);

        var activeTenantCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                HostDashboardSql.CountActiveTenants,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var onlineSessionCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                HostDashboardSql.CountActiveHostSessions,
                new { NowUtc = now },
                cancellationToken)
            .ConfigureAwait(false);
        var todayRequestCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                HostDashboardSql.CountTodayAccessLogs,
                new { StartOfDayUtc = startOfDayUtc },
                cancellationToken)
            .ConfigureAwait(false);
        var todayErrorRate = await queryExecutor.QuerySingleOrDefaultAsync<decimal>(
                ResolveErrorRateStatement(),
                new { StartOfDayUtc = startOfDayUtc },
                cancellationToken)
            .ConfigureAwait(false);
        var activities = await queryExecutor.QueryAsync<HostDashboardActivityRecord>(
                ResolveRecentActivitiesStatement(),
                new { Take = RecentActivityTake },
                cancellationToken)
            .ConfigureAwait(false);

        return Result<HostDashboardSummaryResponse>.Success(
            new HostDashboardSummaryResponse(
                activeTenantCount,
                onlineSessionCount,
                todayRequestCount,
                todayErrorRate,
                activities.Select(MapActivity).ToArray()));
    }

    private static HostDashboardActivityResponse MapActivity(HostDashboardActivityRecord record) =>
        new(
            record.ActionKey,
            record.HttpMethod,
            record.RequestPath,
            record.Succeeded,
            record.OccurredAtUtc);

    private SqlStatement ResolveErrorRateStatement() =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => HostDashboardSql.TodayAccessErrorRateSqlServer,
            DatabaseProvider.MySql => HostDashboardSql.TodayAccessErrorRateMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'."),
        };

    private SqlStatement ResolveRecentActivitiesStatement() =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => HostDashboardSql.ListRecentActivitiesSqlServer,
            DatabaseProvider.MySql => HostDashboardSql.ListRecentActivitiesMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'."),
        };
}
