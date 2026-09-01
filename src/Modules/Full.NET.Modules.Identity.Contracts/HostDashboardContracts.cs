namespace Full.NET.Modules.Identity.Contracts;

/// <summary>为 Host 工作台提供租户模块自有指标的只读端口。</summary>
public interface IHostDashboardTenantMetricsReader
{
    /// <summary>
    /// 读取当前启用租户数量。
    /// </summary>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>处于活动状态的租户计数。</returns>
    Task<long> CountActiveTenantsAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>为 Host 工作台提供审计模块自有指标的只读端口。</summary>
public interface IHostDashboardAuditMetricsReader
{
    /// <summary>
    /// 读取指定 UTC 起点后的访问指标及最近操作；实现方必须保持活动按时间倒序。
    /// </summary>
    /// <param name="startOfDayUtc">统计当日起点（UTC），用于 Today 前缀聚合。</param>
    /// <param name="recentActivityTake">最近活动记录的返回条数上限；调用方应使用受控值避免一次读取过多。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>当日请求汇总与按时间倒序排列的最近活动。</returns>
    Task<HostDashboardAuditMetrics> ReadAsync(
        DateTimeOffset startOfDayUtc,
        int recentActivityTake,
        CancellationToken cancellationToken = default);
}

/// <summary>审计模块向 Host 工作台提供的最小只读投影。</summary>
/// <param name="TodayRequestCount">统计起点至今的请求总量。</param>
/// <param name="TodayErrorRate">统计起点至今的错误率；取值 0-1 之间。</param>
/// <param name="RecentActivities">最近活动记录；顺序按发生时间倒序。</param>
public sealed record HostDashboardAuditMetrics(
    long TodayRequestCount,
    decimal TodayErrorRate,
    HostDashboardActivityResponse[] RecentActivities);

/// <summary>Host 工作台展示的单条最近活动记录。</summary>
/// <param name="ActionKey">稳定审计动作键。</param>
/// <param name="HttpMethod">请求 HTTP 方法。</param>
/// <param name="RequestPath">请求路径。</param>
/// <param name="Succeeded">本次动作是否成功。</param>
/// <param name="OccurredAtUtc">动作发生时间（UTC）。</param>
public sealed record HostDashboardActivityResponse(
    string ActionKey,
    string HttpMethod,
    string RequestPath,
    bool Succeeded,
    DateTimeOffset OccurredAtUtc);

/// <summary>Host 工作台汇总指标，供仪表盘首屏展示。</summary>
/// <param name="ActiveTenantCount">当前活动租户总数。</param>
/// <param name="OnlineSessionCount">当前在线刷新会话数。</param>
/// <param name="TodayRequestCount">统计起点至今的请求总量。</param>
/// <param name="TodayErrorRate">统计起点至今的错误率；取值 0-1 之间。</param>
/// <param name="RecentActivities">最近活动记录；顺序按发生时间倒序。</param>
public sealed record HostDashboardSummaryResponse(
    long ActiveTenantCount,
    long OnlineSessionCount,
    long TodayRequestCount,
    decimal TodayErrorRate,
    HostDashboardActivityResponse[] RecentActivities);
