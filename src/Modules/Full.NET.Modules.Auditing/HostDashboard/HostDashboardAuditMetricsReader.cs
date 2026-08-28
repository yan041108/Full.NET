using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.HostDashboard;

/// <summary>使用 Auditing 自有表为 Host 工作台提供当日访问指标与最近操作。</summary>
internal sealed class HostDashboardAuditMetricsReader(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
    : IHostDashboardAuditMetricsReader
{
    public async Task<HostDashboardAuditMetrics> ReadAsync(
        DateTimeOffset startOfDayUtc,
        int recentActivityTake,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(recentActivityTake, 1);

        var metrics = await queryExecutor.QuerySingleOrDefaultAsync<
                HostDashboardAccessMetricsRecord>(
                HostDashboardAuditSql.ReadTodayAccessMetrics,
                AuditingSqlParameters.Create(("StartOfDayUtc", startOfDayUtc)),
                cancellationToken)
            .ConfigureAwait(false);
        var activities = await queryExecutor.QueryAsync<HostDashboardActivityRecord>(
                ResolveRecentActivitiesStatement(),
                AuditingSqlParameters.Create(("Take", recentActivityTake)),
                cancellationToken)
            .ConfigureAwait(false);

        return new HostDashboardAuditMetrics(
            metrics?.TodayRequestCount ?? 0,
            metrics?.TodayErrorRate ?? 0m,
            activities.Select(MapActivity).ToArray());
    }

    private static HostDashboardActivityResponse MapActivity(
        HostDashboardActivityRecord record) =>
        new(
            record.ActionKey,
            record.HttpMethod,
            record.RequestPath,
            record.Succeeded,
            record.OccurredAtUtc);

    private SqlStatement ResolveRecentActivitiesStatement() =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => HostDashboardAuditSql.ListRecentActivitiesSqlServer,
            DatabaseProvider.MySql => HostDashboardAuditSql.ListRecentActivitiesMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'."),
        };
}
