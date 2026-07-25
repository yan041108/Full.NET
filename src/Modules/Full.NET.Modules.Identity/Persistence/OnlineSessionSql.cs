using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>Host 在线会话只读查询与强制下线相关 SQL。</summary>
internal static class OnlineSessionSql
{
    private const string ActiveSessionPredicate = """
        session.ConsumedAtUtc IS NULL
          AND session.RevokedAtUtc IS NULL
          AND session.ExpiresAtUtc > @NowUtc
          AND identityUser.ScopeKey = 'host'
          AND identityUser.TenantId IS NULL
        """;

    public static readonly SqlStatement CountActiveHostSessionsSqlServer = new(
        "identity.count_active_host_online_sessions.sql_server",
        $"""
        SELECT COUNT(1)
        FROM fn_identity_refresh_session AS session
        INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = session.UserId
        WHERE {ActiveSessionPredicate}
          AND (@UsernameContains IS NULL OR identityUser.Username LIKE '%' + @UsernameContains + '%')
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountActiveHostSessionsMySql = new(
        "identity.count_active_host_online_sessions.mysql",
        $"""
        SELECT COUNT(1)
        FROM fn_identity_refresh_session AS session
        INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = session.UserId
        WHERE {ActiveSessionPredicate}
          AND (@UsernameContains IS NULL OR identityUser.Username LIKE CONCAT('%', @UsernameContains, '%'))
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListActiveHostSessionsSqlServer = new(
        "identity.list_active_host_online_sessions.sql_server",
        $"""
        SELECT session.Id AS SessionId,
               session.UserId,
               identityUser.Username,
               identityUser.DisplayName,
               session.ClientId,
               session.ActiveTenantId,
               session.CreatedAtUtc,
               session.ExpiresAtUtc
        FROM fn_identity_refresh_session AS session
        INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = session.UserId
        WHERE {ActiveSessionPredicate}
          AND (@UsernameContains IS NULL OR identityUser.Username LIKE '%' + @UsernameContains + '%')
        ORDER BY session.CreatedAtUtc DESC, session.Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListActiveHostSessionsMySql = new(
        "identity.list_active_host_online_sessions.mysql",
        $"""
        SELECT session.Id AS SessionId,
               session.UserId,
               identityUser.Username,
               identityUser.DisplayName,
               session.ClientId,
               session.ActiveTenantId,
               session.CreatedAtUtc,
               session.ExpiresAtUtc
        FROM fn_identity_refresh_session AS session
        INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = session.UserId
        WHERE {ActiveSessionPredicate}
          AND (@UsernameContains IS NULL OR identityUser.Username LIKE CONCAT('%', @UsernameContains, '%'))
        ORDER BY session.CreatedAtUtc DESC, session.Id DESC
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindActiveHostSessionById = new(
        "identity.find_active_host_online_session_by_id",
        $"""
        SELECT session.Id AS SessionId,
               session.UserId,
               session.FamilyId,
               identityUser.Username,
               identityUser.DisplayName,
               session.ClientId,
               session.ActiveTenantId,
               session.CreatedAtUtc,
               session.ExpiresAtUtc
        FROM fn_identity_refresh_session AS session
        INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = session.UserId
        WHERE session.Id = @SessionId
          AND {ActiveSessionPredicate}
        """,
        SqlDataScope.HostOnly);
}
