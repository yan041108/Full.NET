using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Features.WriteAccessLogs;
using Full.NET.Modules.Auditing.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.HostDashboard;

/// <summary>复用审计趋势 SQL，为 Host 工作台提供访问流量时间桶。</summary>
internal sealed class HostDashboardAuditTrendReader(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions,
    AuditingTrendTimeRangePolicy trendTimeRangePolicy,
    IOptions<AccessLogCaptureOptions> captureOptions)
    : IHostDashboardAuditTrendReader
{
    /// <inheritdoc />
    public async Task<HostDashboardTrafficTrendResponse?> ReadAccessTrendAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        if (!captureOptions.Value.Enabled)
        {
            return null;
        }

        var planResult = trendTimeRangePolicy.Plan(fromUtc, toUtc, requestedBucketMinutes: null);
        if (!planResult.IsSuccess)
        {
            // 无法规划的时间窗不能展示为已采集但没有流量。
            return null;
        }

        var plan = planResult.Value;
        var rows = await queryExecutor.QueryAsync<AuditLogTrendBucketRecord>(
                ResolveStatement(),
                AuditingSqlParameters.Create(
                    ("FromUtc", plan.FromUtc),
                    ("ToUtc", plan.ToUtc),
                    ("BucketMinutes", plan.BucketSizeMinutes)),
                cancellationToken)
            .ConfigureAwait(false);

        return new HostDashboardTrafficTrendResponse(
            plan.FromUtc,
            plan.ToUtc,
            plan.BucketSizeMinutes,
            rows.Select(MapBucket).ToArray());
    }

    private static HostDashboardTrafficTrendBucketResponse MapBucket(
        AuditLogTrendBucketRecord record) =>
        new(record.BucketStartUtc, record.EventCount, record.ErrorCount);

    private SqlStatement ResolveStatement() =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => AuditLogTrendSql.AccessTrendSqlServer,
            DatabaseProvider.MySql => AuditLogTrendSql.AccessTrendMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'."),
        };
}
