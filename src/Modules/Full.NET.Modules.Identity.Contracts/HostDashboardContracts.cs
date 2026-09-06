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

/// <summary>为 Host 工作台提供审计模块访问流量趋势的只读端口。</summary>
public interface IHostDashboardAuditTrendReader
{
    /// <summary>
    /// 读取指定 UTC 时间窗内的访问流量趋势桶。
    /// </summary>
    /// <param name="fromUtc">统计窗口起点（UTC）。</param>
    /// <param name="toUtc">统计窗口终点（UTC）。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>按时间升序排列的访问流量趋势。</returns>
    Task<HostDashboardTrafficTrendResponse> ReadAccessTrendAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>为 Host 工作台提供工作流待办与实例入口计数的只读端口。</summary>
public interface IHostDashboardWorkflowEntryReader
{
    /// <summary>
    /// 批量读取当前用户在可信作用域内的待办与活动实例计数。
    /// </summary>
    /// <param name="actorUserId">可信当前用户标识。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>待办与“我发起的活动实例”计数。</returns>
    Task<HostDashboardWorkflowEntryMetrics> ReadAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default);
}

/// <summary>工作流向 Host 工作台提供的最小入口计数投影。</summary>
/// <param name="PendingTodoCount">当前用户待办总数。</param>
/// <param name="MyActiveInstanceCount">当前用户发起的活动实例总数。</param>
public sealed record HostDashboardWorkflowEntryMetrics(
    long PendingTodoCount,
    long MyActiveInstanceCount);

/// <summary>Host 工作台各指标片段所需的精确权限码。</summary>
public static class HostDashboardMetricPermissions
{
    /// <summary>读取活动租户计数。</summary>
    public const string ActiveTenants = "tenancy.host_tenants.read";

    /// <summary>读取在线会话计数。</summary>
    public const string OnlineSessions = "identity.sessions.read";

    /// <summary>读取当日访问指标与最近活动。</summary>
    public const string AuditAccess = "auditing.access.read";

    /// <summary>读取访问流量时间趋势。</summary>
    public const string AuditTrends = "auditing.trends.read";

    /// <summary>读取待办业务入口计数。</summary>
    public const string WorkflowTodos = "workflow.todos.read";

    /// <summary>读取“我的实例”业务入口计数。</summary>
    public const string WorkflowInstances = "workflow.instances.read";
}

/// <summary>Host 工作台业务入口稳定键。</summary>
public static class HostDashboardBusinessEntryKeys
{
    /// <summary>工作流待办入口。</summary>
    public const string WorkflowPendingTodos = "workflow.pending_todos";

    /// <summary>我发起的工作流实例入口。</summary>
    public const string WorkflowMyInstances = "workflow.my_instances";
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

/// <summary>Host 工作台访问流量时间桶。</summary>
/// <param name="BucketStartUtc">桶起始时间（UTC）。</param>
/// <param name="EventCount">桶内事件总数。</param>
/// <param name="ErrorCount">桶内错误事件数。</param>
public sealed record HostDashboardTrafficTrendBucketResponse(
    DateTimeOffset BucketStartUtc,
    long EventCount,
    long ErrorCount);

/// <summary>Host 工作台访问流量趋势。</summary>
/// <param name="FromUtc">统计窗口起点（UTC）。</param>
/// <param name="ToUtc">统计窗口终点（UTC）。</param>
/// <param name="BucketSizeMinutes">桶宽（分钟）。</param>
/// <param name="Buckets">按时间升序排列的桶集合。</param>
public sealed record HostDashboardTrafficTrendResponse(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int BucketSizeMinutes,
    HostDashboardTrafficTrendBucketResponse[] Buckets);

/// <summary>Host 工作台业务入口卡片。</summary>
/// <param name="EntryKey">稳定入口键；前端据此选择文案与图标。</param>
/// <param name="Count">待处理或待查看数量。</param>
/// <param name="RoutePath">管理端深链路径。</param>
/// <param name="RequiredPermission">展示与跳转所需的精确权限码。</param>
public sealed record HostDashboardBusinessEntryResponse(
    string EntryKey,
    long Count,
    string RoutePath,
    string RequiredPermission);

/// <summary>Host 工作台汇总指标，供仪表盘首屏展示。</summary>
/// <param name="ActiveTenantCount">当前活动租户总数；无权限时为 <see langword="null"/>。</param>
/// <param name="OnlineSessionCount">当前在线刷新会话数；无权限时为 <see langword="null"/>。</param>
/// <param name="TodayRequestCount">统计起点至今的请求总量；无权限时为 <see langword="null"/>。</param>
/// <param name="TodayErrorRate">统计起点至今的错误率；无权限时为 <see langword="null"/>。</param>
/// <param name="RecentActivities">最近活动记录；无权限时为 <see langword="null"/>。</param>
/// <param name="AccessTrafficTrend">当日访问流量趋势；无权限时为 <see langword="null"/>。</param>
/// <param name="BusinessEntries">当前用户可见的业务入口卡片。</param>
public sealed record HostDashboardSummaryResponse(
    long? ActiveTenantCount,
    long? OnlineSessionCount,
    long? TodayRequestCount,
    decimal? TodayErrorRate,
    HostDashboardActivityResponse[]? RecentActivities,
    HostDashboardTrafficTrendResponse? AccessTrafficTrend,
    HostDashboardBusinessEntryResponse[] BusinessEntries);
