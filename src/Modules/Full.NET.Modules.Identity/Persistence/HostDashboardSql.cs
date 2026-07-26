using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>Host 工作台在 Identity 模块内拥有的在线会话统计 SQL。</summary>
internal static class HostDashboardSql
{
    private const string ActiveHostSessionPredicate = """
        session.ConsumedAtUtc IS NULL
          AND session.RevokedAtUtc IS NULL
          AND session.ExpiresAtUtc > @NowUtc
          AND identityUser.ScopeKey = 'host'
          AND identityUser.TenantId IS NULL
        """;

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
}
