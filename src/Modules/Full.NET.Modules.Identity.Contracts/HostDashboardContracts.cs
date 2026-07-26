namespace Full.NET.Modules.Identity.Contracts;

/// <summary>为 Host 工作台提供租户模块自有指标的只读端口。</summary>
public interface IHostDashboardTenantMetricsReader
{
    /// <summary>读取当前启用租户数量。</summary>
    Task<long> CountActiveTenantsAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>为 Host 工作台提供审计模块自有指标的只读端口。</summary>
public interface IHostDashboardAuditMetricsReader
{
    /// <summary>
    /// 读取指定 UTC 起点后的访问指标及最近操作；实现方必须保持活动按时间倒序。
    /// </summary>
    Task<HostDashboardAuditMetrics> ReadAsync(
        DateTimeOffset startOfDayUtc,
        int recentActivityTake,
        CancellationToken cancellationToken = default);
}

/// <summary>审计模块向 Host 工作台提供的最小只读投影。</summary>
public sealed record HostDashboardAuditMetrics(
    long TodayRequestCount,
    decimal TodayErrorRate,
    HostDashboardActivityResponse[] RecentActivities);

public sealed record HostDashboardActivityResponse(
    string ActionKey,
    string HttpMethod,
    string RequestPath,
    bool Succeeded,
    DateTimeOffset OccurredAtUtc);

public sealed record HostDashboardSummaryResponse(
    long ActiveTenantCount,
    long OnlineSessionCount,
    long TodayRequestCount,
    decimal TodayErrorRate,
    HostDashboardActivityResponse[] RecentActivities);
