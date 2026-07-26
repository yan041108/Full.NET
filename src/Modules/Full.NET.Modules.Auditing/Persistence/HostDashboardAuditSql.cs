using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Auditing.Persistence;

/// <summary>Auditing 自有表对 Host 工作台暴露的最小聚合查询。</summary>
internal static class HostDashboardAuditSql
{
    public static readonly SqlStatement ReadTodayAccessMetrics =
        new(
            "auditing.host_dashboard.read_today_access_metrics",
            """
            SELECT COUNT(1) AS TodayRequestCount,
                   CASE
                       WHEN COUNT(1) = 0 THEN CAST(0 AS decimal(18, 6))
                       ELSE CAST(SUM(CASE WHEN StatusCode >= 500 THEN 1 ELSE 0 END) AS decimal(18, 6))
                            / CAST(COUNT(1) AS decimal(18, 6))
                   END AS TodayErrorRate
            FROM fn_auditing_access_log
            WHERE OccurredAtUtc >= @StartOfDayUtc
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ListRecentActivitiesSqlServer =
        new(
            "auditing.host_dashboard.list_recent_activities.sql_server",
            """
            SELECT TOP (@Take) ActionKey, HttpMethod, RequestPath, Succeeded, OccurredAtUtc
            FROM fn_auditing_operation_log
            ORDER BY OccurredAtUtc DESC, Id DESC
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ListRecentActivitiesMySql =
        new(
            "auditing.host_dashboard.list_recent_activities.mysql",
            """
            SELECT ActionKey, HttpMethod, RequestPath, Succeeded, OccurredAtUtc
            FROM fn_auditing_operation_log
            ORDER BY OccurredAtUtc DESC, Id DESC
            LIMIT @Take
            """,
            SqlDataScope.HostOnly);
}
