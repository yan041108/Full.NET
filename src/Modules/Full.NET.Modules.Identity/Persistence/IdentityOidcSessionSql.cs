using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

internal static class IdentityOidcSessionSql
{
    private const string CenterSessionColumns = """
        center.Id, center.UserId, center.SecurityStamp, center.CreatedAtUtc, center.ExpiresAtUtc,
        center.RevokedAtUtc, center.Version, center.UpdatedAtUtc
        """;

    private const string ApplicationSessionColumns = """
        app.Id, app.CenterSessionId, app.OidcApplicationId, app.ClientId, app.UserId, app.ActorScope,
        app.EffectiveScope, app.ActiveTenantId, app.CreatedAtUtc, app.ExpiresAtUtc, app.RevokedAtUtc,
        app.Version, app.UpdatedAtUtc
        """;

    private const string ActiveApplicationSessionPredicate = """
        app.RevokedAtUtc IS NULL
          AND app.ExpiresAtUtc > @NowUtc
          AND center.RevokedAtUtc IS NULL
          AND center.ExpiresAtUtc > @NowUtc
        """;

    private const string ActiveHostApplicationSessionPredicate = $"""
        {ActiveApplicationSessionPredicate}
          AND identityUser.ScopeKey = 'host'
          AND identityUser.TenantId IS NULL
        """;

    public static readonly SqlStatement InsertCenterSession = new(
        "identity.insert_oidc_center_session",
        """
        INSERT INTO fn_identity_oidc_center_session
            (Id, UserId, SecurityStamp, CreatedAtUtc, ExpiresAtUtc, RevokedAtUtc, Version, UpdatedAtUtc)
        VALUES
            (@Id, @UserId, @SecurityStamp, @CreatedAtUtc, @ExpiresAtUtc, @RevokedAtUtc, @Version, @UpdatedAtUtc)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateCenterSession = new(
        "identity.update_oidc_center_session",
        """
        UPDATE fn_identity_oidc_center_session
        SET SecurityStamp = @SecurityStamp, ExpiresAtUtc = @ExpiresAtUtc, RevokedAtUtc = @RevokedAtUtc,
            UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1
        WHERE Id = @Id AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement RevokeCenterSession = new(
        "identity.revoke_oidc_center_session",
        """
        UPDATE fn_identity_oidc_center_session
        SET RevokedAtUtc = @RevokedAtUtc, UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1
        WHERE Id = @Id AND RevokedAtUtc IS NULL AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement RevokeAllCenterSessionsByUser = new(
        "identity.revoke_all_oidc_center_sessions_by_user",
        """
        UPDATE fn_identity_oidc_center_session
        SET RevokedAtUtc = @RevokedAtUtc, UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1
        WHERE UserId = @UserId AND RevokedAtUtc IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindCenterSessionById = new(
        "identity.find_oidc_center_session_by_id",
        $"SELECT {CenterSessionColumns} FROM fn_identity_oidc_center_session AS center WHERE center.Id = @Id",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindActiveCenterSessionById = new(
        "identity.find_active_oidc_center_session_by_id",
        $"""
        SELECT {CenterSessionColumns}
        FROM fn_identity_oidc_center_session AS center
        WHERE center.Id = @Id
          AND center.RevokedAtUtc IS NULL
          AND center.ExpiresAtUtc > @NowUtc
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertApplicationSession = new(
        "identity.insert_oidc_application_session",
        """
        INSERT INTO fn_identity_oidc_application_session
            (Id, CenterSessionId, OidcApplicationId, ClientId, UserId, ActorScope, EffectiveScope,
             ActiveTenantId, CreatedAtUtc, ExpiresAtUtc, RevokedAtUtc, Version, UpdatedAtUtc)
        VALUES
            (@Id, @CenterSessionId, @OidcApplicationId, @ClientId, @UserId, @ActorScope, @EffectiveScope,
             @ActiveTenantId, @CreatedAtUtc, @ExpiresAtUtc, @RevokedAtUtc, @Version, @UpdatedAtUtc)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement RevokeApplicationSession = new(
        "identity.revoke_oidc_application_session",
        """
        UPDATE fn_identity_oidc_application_session
        SET RevokedAtUtc = @RevokedAtUtc, UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1
        WHERE Id = @Id AND RevokedAtUtc IS NULL AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement RevokeApplicationSessionsByCenterSession = new(
        "identity.revoke_oidc_application_sessions_by_center_session",
        """
        UPDATE fn_identity_oidc_application_session
        SET RevokedAtUtc = @RevokedAtUtc, UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1
        WHERE CenterSessionId = @CenterSessionId AND RevokedAtUtc IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement RevokeAllApplicationSessionsByUser = new(
        "identity.revoke_all_oidc_application_sessions_by_user",
        """
        UPDATE fn_identity_oidc_application_session
        SET RevokedAtUtc = @RevokedAtUtc, UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1
        WHERE UserId = @UserId AND RevokedAtUtc IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement RevokeAllActiveApplicationSessionsByClientId = new(
        "identity.revoke_all_active_oidc_application_sessions_by_client_id",
        """
        UPDATE fn_identity_oidc_application_session
        SET RevokedAtUtc = @RevokedAtUtc, UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1
        WHERE ClientId = @ClientId AND RevokedAtUtc IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindApplicationSessionById = new(
        "identity.find_oidc_application_session_by_id",
        $"SELECT {ApplicationSessionColumns} FROM fn_identity_oidc_application_session AS app WHERE app.Id = @Id",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindActiveApplicationSessionById = new(
        "identity.find_active_oidc_application_session_by_id",
        $"""
        SELECT {ApplicationSessionColumns}
        FROM fn_identity_oidc_application_session AS app
        INNER JOIN fn_identity_oidc_center_session AS center ON center.Id = app.CenterSessionId
        WHERE app.Id = @Id
          AND {ActiveApplicationSessionPredicate}
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindApplicationSessionValidationById = new(
        "identity.find_oidc_application_session_validation_by_id",
        """
        SELECT app.Id AS ApplicationSessionId,
               app.CenterSessionId,
               app.UserId,
               app.ClientId,
               app.ActorScope,
               app.EffectiveScope,
               app.ActiveTenantId,
               app.ExpiresAtUtc AS ApplicationExpiresAtUtc,
               app.RevokedAtUtc AS ApplicationRevokedAtUtc,
               center.SecurityStamp AS CenterSecurityStamp,
               center.ExpiresAtUtc AS CenterExpiresAtUtc,
               center.RevokedAtUtc AS CenterRevokedAtUtc,
               identityUser.IsActive,
               identityUser.LockoutEndUtc,
               identityUser.SecurityStamp AS UserSecurityStamp,
               identityUser.MustChangePassword,
               identityUser.PasswordChangedAtUtc
        FROM fn_identity_oidc_application_session AS app
        INNER JOIN fn_identity_oidc_center_session AS center ON center.Id = app.CenterSessionId
        INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = app.UserId
        WHERE app.Id = @ApplicationSessionId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindHostOidcApplicationSessionById = new(
        "identity.find_host_oidc_application_session_by_id",
        """
        SELECT app.Id AS SessionId,
               app.UserId,
               identityUser.Username,
               identityUser.DisplayName,
               app.ClientId,
               app.ActiveTenantId,
               app.CreatedAtUtc,
               app.ExpiresAtUtc
        FROM fn_identity_oidc_application_session AS app
        INNER JOIN fn_identity_oidc_center_session AS center ON center.Id = app.CenterSessionId
        INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = app.UserId
        WHERE app.Id = @SessionId
          AND identityUser.ScopeKey = 'host'
          AND identityUser.TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindActiveHostApplicationSessionById = new(
        "identity.find_active_host_oidc_application_session_by_id",
        $"""
        SELECT app.Id AS SessionId,
               app.UserId,
               identityUser.Username,
               identityUser.DisplayName,
               app.ClientId,
               app.ActiveTenantId,
               app.CreatedAtUtc,
               app.ExpiresAtUtc
        FROM fn_identity_oidc_application_session AS app
        INNER JOIN fn_identity_oidc_center_session AS center ON center.Id = app.CenterSessionId
        INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = app.UserId
        WHERE app.Id = @SessionId
          AND {ActiveHostApplicationSessionPredicate}
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListActiveHostOidcApplicationSessionIdsByUser = new(
        "identity.list_active_host_oidc_application_session_ids_by_user",
        $"""
        SELECT app.Id
        FROM fn_identity_oidc_application_session AS app
        INNER JOIN fn_identity_oidc_center_session AS center ON center.Id = app.CenterSessionId
        INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = app.UserId
        WHERE app.UserId = @UserId
          AND {ActiveHostApplicationSessionPredicate}
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListActiveHostOidcApplicationSessionIdsByUserAndClient = new(
        "identity.list_active_host_oidc_application_session_ids_by_user_and_client",
        $"""
        SELECT app.Id
        FROM fn_identity_oidc_application_session AS app
        INNER JOIN fn_identity_oidc_center_session AS center ON center.Id = app.CenterSessionId
        INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = app.UserId
        WHERE app.UserId = @UserId
          AND app.ClientId = @ClientId
          AND {ActiveHostApplicationSessionPredicate}
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListActiveHostOidcApplicationSessionOwnershipByClientId = new(
        "identity.list_active_host_oidc_application_session_ownership_by_client_id",
        $"""
        SELECT app.Id AS SessionId, app.UserId
        FROM fn_identity_oidc_application_session AS app
        INNER JOIN fn_identity_oidc_center_session AS center ON center.Id = app.CenterSessionId
        INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = app.UserId
        WHERE app.ClientId = @ClientId
          AND {ActiveHostApplicationSessionPredicate}
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountActiveHostSessionsCombinedSqlServer = new(
        "identity.count_active_host_online_sessions_combined.sql_server",
        $"""
        SELECT COUNT(1)
        FROM (
            SELECT session.Id
            FROM fn_identity_refresh_session AS session
            INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = session.UserId
            WHERE session.ConsumedAtUtc IS NULL
              AND session.RevokedAtUtc IS NULL
              AND session.ExpiresAtUtc > @NowUtc
              AND identityUser.ScopeKey = 'host'
              AND identityUser.TenantId IS NULL
              AND (@UserId IS NULL OR session.UserId = @UserId)
              AND (@UsernameContains IS NULL OR identityUser.Username LIKE '%' + @UsernameContains + '%')
            UNION ALL
            SELECT app.Id
            FROM fn_identity_oidc_application_session AS app
            INNER JOIN fn_identity_oidc_center_session AS center ON center.Id = app.CenterSessionId
            INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = app.UserId
            WHERE {ActiveHostApplicationSessionPredicate}
              AND (@UserId IS NULL OR app.UserId = @UserId)
              AND (@UsernameContains IS NULL OR identityUser.Username LIKE '%' + @UsernameContains + '%')
        ) AS combined
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountActiveHostSessionsCombinedMySql = new(
        "identity.count_active_host_online_sessions_combined.mysql",
        $"""
        SELECT COUNT(1)
        FROM (
            SELECT session.Id
            FROM fn_identity_refresh_session AS session
            INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = session.UserId
            WHERE session.ConsumedAtUtc IS NULL
              AND session.RevokedAtUtc IS NULL
              AND session.ExpiresAtUtc > @NowUtc
              AND identityUser.ScopeKey = 'host'
              AND identityUser.TenantId IS NULL
              AND (@UserId IS NULL OR session.UserId = @UserId)
              AND (@UsernameContains IS NULL OR identityUser.Username LIKE CONCAT('%', @UsernameContains, '%'))
            UNION ALL
            SELECT app.Id
            FROM fn_identity_oidc_application_session AS app
            INNER JOIN fn_identity_oidc_center_session AS center ON center.Id = app.CenterSessionId
            INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = app.UserId
            WHERE {ActiveHostApplicationSessionPredicate}
              AND (@UserId IS NULL OR app.UserId = @UserId)
              AND (@UsernameContains IS NULL OR identityUser.Username LIKE CONCAT('%', @UsernameContains, '%'))
        ) AS combined
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListActiveHostSessionsCombinedSqlServer = new(
        "identity.list_active_host_online_sessions_combined.sql_server",
        $"""
        SELECT combined.SessionId,
               combined.UserId,
               combined.Username,
               combined.DisplayName,
               combined.ClientId,
               combined.ActiveTenantId,
               combined.CreatedAtUtc,
               combined.ExpiresAtUtc
        FROM (
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
            WHERE session.ConsumedAtUtc IS NULL
              AND session.RevokedAtUtc IS NULL
              AND session.ExpiresAtUtc > @NowUtc
              AND identityUser.ScopeKey = 'host'
              AND identityUser.TenantId IS NULL
              AND (@UserId IS NULL OR session.UserId = @UserId)
              AND (@UsernameContains IS NULL OR identityUser.Username LIKE '%' + @UsernameContains + '%')
            UNION ALL
            SELECT app.Id AS SessionId,
                   app.UserId,
                   identityUser.Username,
                   identityUser.DisplayName,
                   app.ClientId,
                   app.ActiveTenantId,
                   app.CreatedAtUtc,
                   app.ExpiresAtUtc
            FROM fn_identity_oidc_application_session AS app
            INNER JOIN fn_identity_oidc_center_session AS center ON center.Id = app.CenterSessionId
            INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = app.UserId
            WHERE {ActiveHostApplicationSessionPredicate}
              AND (@UserId IS NULL OR app.UserId = @UserId)
              AND (@UsernameContains IS NULL OR identityUser.Username LIKE '%' + @UsernameContains + '%')
        ) AS combined
        ORDER BY combined.CreatedAtUtc DESC, combined.SessionId DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListActiveHostSessionsCombinedMySql = new(
        "identity.list_active_host_online_sessions_combined.mysql",
        $"""
        SELECT combined.SessionId,
               combined.UserId,
               combined.Username,
               combined.DisplayName,
               combined.ClientId,
               combined.ActiveTenantId,
               combined.CreatedAtUtc,
               combined.ExpiresAtUtc
        FROM (
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
            WHERE session.ConsumedAtUtc IS NULL
              AND session.RevokedAtUtc IS NULL
              AND session.ExpiresAtUtc > @NowUtc
              AND identityUser.ScopeKey = 'host'
              AND identityUser.TenantId IS NULL
              AND (@UserId IS NULL OR session.UserId = @UserId)
              AND (@UsernameContains IS NULL OR identityUser.Username LIKE CONCAT('%', @UsernameContains, '%'))
            UNION ALL
            SELECT app.Id AS SessionId,
                   app.UserId,
                   identityUser.Username,
                   identityUser.DisplayName,
                   app.ClientId,
                   app.ActiveTenantId,
                   app.CreatedAtUtc,
                   app.ExpiresAtUtc
            FROM fn_identity_oidc_application_session AS app
            INNER JOIN fn_identity_oidc_center_session AS center ON center.Id = app.CenterSessionId
            INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = app.UserId
            WHERE {ActiveHostApplicationSessionPredicate}
              AND (@UserId IS NULL OR app.UserId = @UserId)
              AND (@UsernameContains IS NULL OR identityUser.Username LIKE CONCAT('%', @UsernameContains, '%'))
        ) AS combined
        ORDER BY combined.CreatedAtUtc DESC, combined.SessionId DESC
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);
}
