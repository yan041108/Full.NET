using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>Host 工作台只读聚合 SQL；跨表统计仅供仪表盘展示，不承载写路径。</summary>
internal static class HostDashboardSql
{
    private const string ActiveHostSessionPredicate = """
        session.ConsumedAtUtc IS NULL
          AND session.RevokedAtUtc IS NULL
          AND session.ExpiresAtUtc > @NowUtc
          AND identityUser.ScopeKey = 'host'
          AND identityUser.TenantId IS NULL
        """;

    public static readonly SqlStatement CountActiveTenants =
        new(
            "platform.count_active_tenants",
            """
            SELECT COUNT(1)
            FROM fn_tenancy_tenant
            WHERE IsActive = 1
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement CountActiveHostSessions =
        new(
            "platform.count_active_host_online_sessions",
            $"""
            SELECT COUNT(1)
            FROM fn_identity_refresh_session AS session
            INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = session.UserId
            WHERE {ActiveHostSessionPredicate}
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement CountTodayAccessLogs =
        new(
            "platform.count_today_access_logs",
            """
            SELECT COUNT(1)
            FROM fn_auditing_access_log
            WHERE OccurredAtUtc >= @StartOfDayUtc
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement TodayAccessErrorRateSqlServer =
        new(
            "platform.today_access_error_rate.sql_server",
            """
            SELECT CASE
                WHEN COUNT(1) = 0 THEN CAST(0 AS decimal(18, 6))
                ELSE CAST(SUM(CASE WHEN StatusCode >= 500 THEN 1 ELSE 0 END) AS decimal(18, 6))
                     / CAST(COUNT(1) AS decimal(18, 6))
            END
            FROM fn_auditing_access_log
            WHERE OccurredAtUtc >= @StartOfDayUtc
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement TodayAccessErrorRateMySql =
        new(
            "platform.today_access_error_rate.mysql",
            """
            SELECT CASE
                WHEN COUNT(1) = 0 THEN CAST(0 AS decimal(18, 6))
                ELSE CAST(SUM(CASE WHEN StatusCode >= 500 THEN 1 ELSE 0 END) AS decimal(18, 6))
                     / CAST(COUNT(1) AS decimal(18, 6))
            END
            FROM fn_auditing_access_log
            WHERE OccurredAtUtc >= @StartOfDayUtc
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ListRecentActivitiesSqlServer =
        new(
            "platform.list_recent_operation_logs.sql_server",
            """
            SELECT TOP (@Take) ActionKey, HttpMethod, RequestPath, Succeeded, OccurredAtUtc
            FROM fn_auditing_operation_log
            ORDER BY OccurredAtUtc DESC, Id DESC
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement ListRecentActivitiesMySql =
        new(
            "platform.list_recent_operation_logs.mysql",
            """
            SELECT ActionKey, HttpMethod, RequestPath, Succeeded, OccurredAtUtc
            FROM fn_auditing_operation_log
            ORDER BY OccurredAtUtc DESC, Id DESC
            LIMIT @Take
            """,
            SqlDataScope.HostOnly);
}
