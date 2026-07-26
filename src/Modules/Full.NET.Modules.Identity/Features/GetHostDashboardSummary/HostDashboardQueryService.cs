using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.GetHostDashboardSummary;

/// <summary>聚合 Host 工作台指标；跨模块数据只通过所有者实现的只读端口获取。</summary>
internal sealed class HostDashboardQueryService(
    IQueryExecutor queryExecutor,
    IClock clock,
    IEnumerable<IHostDashboardTenantMetricsReader> tenantMetricsReaders,
    IEnumerable<IHostDashboardAuditMetricsReader> auditMetricsReaders)
{
    private const int RecentActivityTake = 5;

    public async Task<Result<HostDashboardSummaryResponse>> GetSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var startOfDayUtc = new DateTimeOffset(
            now.UtcDateTime.Date,
            TimeSpan.Zero);

        var tenantMetricsReader = tenantMetricsReaders.SingleOrDefault();
        var auditMetricsReader = auditMetricsReaders.SingleOrDefault();
        var activeTenantCount = tenantMetricsReader is null
            ? 0L
            : await tenantMetricsReader.CountActiveTenantsAsync(cancellationToken)
                .ConfigureAwait(false);
        var onlineSessionCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                HostDashboardSql.CountActiveHostSessions,
                new { NowUtc = now },
                cancellationToken)
            .ConfigureAwait(false);
        var auditMetrics = auditMetricsReader is null
            ? new HostDashboardAuditMetrics(
                0,
                0m,
                Array.Empty<HostDashboardActivityResponse>())
            : await auditMetricsReader.ReadAsync(
                    startOfDayUtc,
                    RecentActivityTake,
                    cancellationToken)
                .ConfigureAwait(false);

        return Result<HostDashboardSummaryResponse>.Success(
            new HostDashboardSummaryResponse(
                activeTenantCount,
                onlineSessionCount,
                auditMetrics.TodayRequestCount,
                auditMetrics.TodayErrorRate,
                auditMetrics.RecentActivities));
    }
}
