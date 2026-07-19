using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

internal static class IdentitySql
{
    public static readonly SqlStatement FindUserByScopeAndUsername = new(
        "identity.find_user_by_scope_and_username",
        """
        SELECT Id, TenantId, ScopeKey, Username, NormalizedUsername, DisplayName,
               PasswordHash, IsActive, FailedLoginCount, LockoutEndUtc,
               SecurityStamp, CreatedAtUtc, UpdatedAtUtc, Version,
               PreferredLocale, ProfileVersion
        FROM fn_identity_user
        WHERE ScopeKey = @ScopeKey AND NormalizedUsername = @NormalizedUsername
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindHostUserById = new(
        "identity.find_host_user_by_id",
        """
        SELECT Id, TenantId, ScopeKey, Username, NormalizedUsername, DisplayName,
               PasswordHash, IsActive, FailedLoginCount, LockoutEndUtc,
               SecurityStamp, CreatedAtUtc, UpdatedAtUtc, Version,
               PreferredLocale, ProfileVersion
        FROM fn_identity_user
        WHERE Id = @UserId AND ScopeKey = 'host' AND TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertUser = new(
        "identity.insert_user",
        """
        INSERT INTO fn_identity_user
            (Id, TenantId, ScopeKey, Username, NormalizedUsername, DisplayName,
             PasswordHash, IsActive, FailedLoginCount, LockoutEndUtc,
             SecurityStamp, CreatedAtUtc, UpdatedAtUtc, Version,
             PreferredLocale, ProfileVersion)
        VALUES
            (@Id, @TenantId, @ScopeKey, @Username, @NormalizedUsername, @DisplayName,
             @PasswordHash, @IsActive, @FailedLoginCount, @LockoutEndUtc,
             @SecurityStamp, @CreatedAtUtc, @UpdatedAtUtc, @Version,
             @PreferredLocale, @ProfileVersion)
        """,
        SqlDataScope.HostOnly);

    // 会话上下文可能已经进入租户；仅允许服务端使用已签名的 sub 与 sid 调用这些 Global 语句。
    public static readonly SqlStatement FindRefreshSessionById = new(
        "identity.find_refresh_session_by_explicit_session_id",
        """
        SELECT session.Id AS SessionId,
               session.UserId,
               session.FamilyId,
               session.ClientId,
               session.TokenHash,
               session.ExpiresAtUtc,
               session.ConsumedAtUtc,
               session.RevokedAtUtc,
               session.ReplacedById,
               session.ActiveTenantId,
               session.CreatedAtUtc,
               session.Version AS SessionVersion,
               identityUser.TenantId,
               identityUser.ScopeKey,
               identityUser.Username,
               identityUser.NormalizedUsername,
               identityUser.DisplayName,
               identityUser.PasswordHash,
               identityUser.IsActive,
               identityUser.FailedLoginCount,
               identityUser.LockoutEndUtc,
               identityUser.SecurityStamp,
               identityUser.CreatedAtUtc AS UserCreatedAtUtc,
               identityUser.UpdatedAtUtc AS UserUpdatedAtUtc,
               identityUser.Version AS UserVersion,
               identityUser.PreferredLocale,
               identityUser.ProfileVersion
        FROM fn_identity_refresh_session AS session
        INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = session.UserId
        WHERE session.Id = @SessionId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement UpdateRefreshSessionContext = new(
        "identity.update_refresh_session_explicit_context",
        """
        UPDATE fn_identity_refresh_session
        SET ActiveTenantId = @ActiveTenantId,
            Version = Version + 1
        WHERE Id = @SessionId
          AND UserId = @UserId
          AND Version = @Version
          AND ConsumedAtUtc IS NULL
          AND RevokedAtUtc IS NULL
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertContextAudit = new(
        "identity.insert_explicit_context_audit",
        """
        INSERT INTO fn_identity_auth_audit
            (Id, UserId, SessionId, UsernameFingerprint, EventType,
             ResultCode, Succeeded, IpAddress, UserAgent, ContextTenantId,
             OccurredAtUtc)
        VALUES
            (@Id, @UserId, @SessionId, @UsernameFingerprint, @EventType,
             @ResultCode, @Succeeded, @IpAddress, @UserAgent, @ContextTenantId,
             @OccurredAtUtc)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindRoleByScopeAndCode = new(
        "identity.find_role_by_scope_and_code",
        """
        SELECT Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
               IsSuperAdministrator,
               CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_identity_role
        WHERE ScopeKey = @ScopeKey AND Code = @Code
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertRole = new(
        "identity.insert_role",
        """
        INSERT INTO fn_identity_role
            (Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
             IsSuperAdministrator,
             CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @ScopeKey, @Code, @Name, @IsSystem, @IsActive,
             @IsSuperAdministrator,
             @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateSystemRole = new(
        "identity.update_system_role",
        """
        UPDATE fn_identity_role
        SET Name = @Name,
            IsSystem = 1,
            IsActive = 1,
            IsSuperAdministrator = @IsSuperAdministrator,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement GetRolePermissionCodes = new(
        "identity.get_role_permission_codes",
        """
        SELECT PermissionCode
        FROM fn_identity_role_permission
        WHERE RoleId = @RoleId
        ORDER BY PermissionCode
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement EnsureRolePermission = new(
        "identity.ensure_role_permission",
        """
        INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
        SELECT @RoleId, @PermissionCode
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM fn_identity_role_permission
            WHERE RoleId = @RoleId AND PermissionCode = @PermissionCode
        )
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement EnsureUserRole = new(
        "identity.ensure_user_role",
        """
        INSERT INTO fn_identity_user_role (UserId, RoleId)
        SELECT @UserId, @RoleId
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM fn_identity_user_role
            WHERE UserId = @UserId AND RoleId = @RoleId
        )
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement LockSuperAdministratorRoleSqlServer = new(
        "identity.lock_super_administrator_role.sql_server",
        """
        SELECT Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
               IsSuperAdministrator, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_identity_role WITH (UPDLOCK, HOLDLOCK)
        WHERE ScopeKey = 'host' AND Code = 'host-administrator'
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement LockSuperAdministratorRoleMySql = new(
        "identity.lock_super_administrator_role.my_sql",
        """
        SELECT Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
               IsSuperAdministrator, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_identity_role
        WHERE ScopeKey = 'host' AND Code = 'host-administrator'
        FOR UPDATE
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountActiveSuperAdministratorAssignment = new(
        "identity.count_active_super_administrator_assignment",
        """
        SELECT COUNT(*)
        FROM fn_identity_user AS identityUser
        INNER JOIN fn_identity_user_role AS userRole
            ON userRole.UserId = identityUser.Id
        INNER JOIN fn_identity_role AS roleObject
            ON roleObject.Id = userRole.RoleId
        WHERE identityUser.Id = @UserId
          AND identityUser.ScopeKey = 'host'
          AND identityUser.TenantId IS NULL
          AND identityUser.IsActive = 1
          AND roleObject.ScopeKey = 'host'
          AND roleObject.TenantId IS NULL
          AND roleObject.IsActive = 1
          AND roleObject.IsSuperAdministrator = 1
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountActiveSuperAdministrators = new(
        "identity.count_active_super_administrators",
        """
        SELECT COUNT(*)
        FROM fn_identity_user AS identityUser
        INNER JOIN fn_identity_user_role AS userRole
            ON userRole.UserId = identityUser.Id
        INNER JOIN fn_identity_role AS roleObject
            ON roleObject.Id = userRole.RoleId
        WHERE identityUser.ScopeKey = 'host'
          AND identityUser.TenantId IS NULL
          AND identityUser.IsActive = 1
          AND roleObject.ScopeKey = 'host'
          AND roleObject.TenantId IS NULL
          AND roleObject.IsActive = 1
          AND roleObject.IsSuperAdministrator = 1
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountActiveHostUser = new(
        "identity.count_active_host_user",
        """
        SELECT COUNT(*)
        FROM fn_identity_user
        WHERE Id = @UserId
          AND ScopeKey = 'host'
          AND TenantId IS NULL
          AND IsActive = 1
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteSuperAdministratorAssignment = new(
        "identity.delete_super_administrator_assignment",
        """
        DELETE FROM fn_identity_user_role
        WHERE UserId = @UserId AND RoleId = @RoleId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListSuperAdministrators = new(
        "identity.list_super_administrators",
        """
        SELECT identityUser.Id AS UserId,
               identityUser.Username,
               identityUser.DisplayName,
               identityUser.IsActive
        FROM fn_identity_user AS identityUser
        INNER JOIN fn_identity_user_role AS userRole
            ON userRole.UserId = identityUser.Id
        INNER JOIN fn_identity_role AS roleObject
            ON roleObject.Id = userRole.RoleId
        WHERE identityUser.ScopeKey = 'host'
          AND identityUser.TenantId IS NULL
          AND roleObject.ScopeKey = 'host'
          AND roleObject.TenantId IS NULL
          AND roleObject.IsSuperAdministrator = 1
        ORDER BY identityUser.NormalizedUsername
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListSuperAdministratorAuditsSqlServer = new(
        "identity.list_super_administrator_audits.sql_server",
        """
        SELECT TOP (@Limit)
               Id, UserId AS TargetUserId, ActorUserId,
               EventType, ResultCode, Succeeded, OccurredAtUtc
        FROM fn_identity_auth_audit
        WHERE EventType IN
            ('identity.super_administrator.granted',
             'identity.super_administrator.revoked')
        ORDER BY OccurredAtUtc DESC, Id DESC
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListSuperAdministratorAuditsMySql = new(
        "identity.list_super_administrator_audits.mysql",
        """
        SELECT Id, UserId AS TargetUserId, ActorUserId,
               EventType, ResultCode, Succeeded, OccurredAtUtc
        FROM fn_identity_auth_audit
        WHERE EventType IN
            ('identity.super_administrator.granted',
             'identity.super_administrator.revoked')
        ORDER BY OccurredAtUtc DESC, Id DESC
        LIMIT @Limit
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertSuperAdministratorAudit = new(
        "identity.insert_super_administrator_audit",
        """
        INSERT INTO fn_identity_auth_audit
            (Id, UserId, SessionId, UsernameFingerprint, EventType,
             ResultCode, Succeeded, IpAddress, UserAgent, ContextTenantId,
             OccurredAtUtc, ActorUserId)
        VALUES
            (@Id, @UserId, @SessionId, @UsernameFingerprint, @EventType,
             @ResultCode, @Succeeded, @IpAddress, @UserAgent, @ContextTenantId,
             @OccurredAtUtc, @ActorUserId)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement RotateSecurityStamp = new(
        "identity.rotate_security_stamp",
        """
        UPDATE fn_identity_user
        SET SecurityStamp = @SecurityStamp,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @UserId AND ScopeKey = 'host'
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement RevokeAllUserSessions = new(
        "identity.revoke_all_user_sessions",
        """
        UPDATE fn_identity_refresh_session
        SET RevokedAtUtc = @RevokedAtUtc,
            Version = Version + 1
        WHERE UserId = @UserId AND RevokedAtUtc IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement GetUserAuthorization = new(
        "identity.get_actor_authorization",
        """
        SELECT rolePermission.PermissionCode,
               roleObject.IsSuperAdministrator
        FROM fn_identity_user_role AS userRole
        INNER JOIN fn_identity_role AS roleObject ON roleObject.Id = userRole.RoleId
        LEFT JOIN fn_identity_role_permission AS rolePermission
            ON rolePermission.RoleId = roleObject.Id
        WHERE userRole.UserId = @UserId
          AND roleObject.IsActive = 1
          AND roleObject.ScopeKey = @ScopeKey
          AND
          (
              (@ScopeKey = 'host' AND roleObject.TenantId IS NULL)
              OR (@ScopeKey <> 'host' AND roleObject.TenantId = @TenantId)
          )
        ORDER BY rolePermission.PermissionCode
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement UpdateLoginFailure = new(
        "identity.update_login_failure",
        """
        UPDATE fn_identity_user
        SET FailedLoginCount = @FailedLoginCount,
            LockoutEndUtc = @LockoutEndUtc,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateLoginSuccess = new(
        "identity.update_login_success",
        """
        UPDATE fn_identity_user
        SET PasswordHash = @PasswordHash,
            FailedLoginCount = 0,
            LockoutEndUtc = NULL,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertRefreshSession = new(
        "identity.insert_refresh_session",
        """
        INSERT INTO fn_identity_refresh_session
            (Id, UserId, FamilyId, ClientId, TokenHash, ExpiresAtUtc,
             ConsumedAtUtc, RevokedAtUtc, ReplacedById, ActiveTenantId,
             CreatedAtUtc, Version)
        VALUES
            (@Id, @UserId, @FamilyId, @ClientId, @TokenHash, @ExpiresAtUtc,
             @ConsumedAtUtc, @RevokedAtUtc, @ReplacedById, @ActiveTenantId,
             @CreatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertAuthAudit = new(
        "identity.insert_auth_audit",
        """
        INSERT INTO fn_identity_auth_audit
            (Id, UserId, SessionId, UsernameFingerprint, EventType,
             ResultCode, Succeeded, IpAddress, UserAgent, ContextTenantId,
             OccurredAtUtc)
        VALUES
            (@Id, @UserId, @SessionId, @UsernameFingerprint, @EventType,
             @ResultCode, @Succeeded, @IpAddress, @UserAgent, @ContextTenantId,
             @OccurredAtUtc)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountAuthenticationAudits = new(
        "identity.count_authentication_audits",
        "SELECT COUNT(*) FROM fn_identity_auth_audit",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindRefreshSessionByHash = new(
        "identity.find_refresh_session_by_hash",
        """
        SELECT session.Id AS SessionId,
               session.UserId,
               session.FamilyId,
               session.ClientId,
               session.TokenHash,
               session.ExpiresAtUtc,
               session.ConsumedAtUtc,
               session.RevokedAtUtc,
               session.ReplacedById,
               session.ActiveTenantId,
               session.CreatedAtUtc,
               session.Version AS SessionVersion,
               identityUser.TenantId,
               identityUser.ScopeKey,
               identityUser.Username,
               identityUser.NormalizedUsername,
               identityUser.DisplayName,
               identityUser.PasswordHash,
               identityUser.IsActive,
               identityUser.FailedLoginCount,
               identityUser.LockoutEndUtc,
               identityUser.SecurityStamp,
               identityUser.CreatedAtUtc AS UserCreatedAtUtc,
               identityUser.UpdatedAtUtc AS UserUpdatedAtUtc,
               identityUser.Version AS UserVersion,
               identityUser.PreferredLocale,
               identityUser.ProfileVersion
        FROM fn_identity_refresh_session AS session
        INNER JOIN fn_identity_user AS identityUser ON identityUser.Id = session.UserId
        WHERE session.TokenHash = @TokenHash
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ConsumeRefreshSession = new(
        "identity.consume_refresh_session",
        """
        UPDATE fn_identity_refresh_session
        SET ConsumedAtUtc = @ConsumedAtUtc,
            ReplacedById = @ReplacedById,
            Version = Version + 1
        WHERE Id = @Id
          AND Version = @Version
          AND ConsumedAtUtc IS NULL
          AND RevokedAtUtc IS NULL
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement RevokeRefreshFamily = new(
        "identity.revoke_refresh_family",
        """
        UPDATE fn_identity_refresh_session
        SET RevokedAtUtc = @RevokedAtUtc,
            Version = Version + 1
        WHERE FamilyId = @FamilyId AND RevokedAtUtc IS NULL
        """,
        SqlDataScope.HostOnly);

    // Global 查询仅接受 JWT 验证后的 sub 与演员原始作用域，两项必须同时命中。
    public static readonly SqlStatement FindProfileByIdentity = new(
        "identity.find_profile_by_verified_identity",
        """
        SELECT Id, ScopeKey, Username, DisplayName, IsActive,
               PreferredLocale, ProfileVersion
        FROM fn_identity_user
        WHERE Id = @UserId AND ScopeKey = @ScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement UpdateLocalePreference = new(
        "identity.update_locale_preference_by_verified_identity",
        """
        UPDATE fn_identity_user
        SET PreferredLocale = @PreferredLocale,
            ProfileVersion = ProfileVersion + 1
        WHERE Id = @UserId
          AND ScopeKey = @ScopeKey
          AND ProfileVersion = @ProfileVersion
          AND IsActive = 1
          AND SecurityStamp = @SecurityStamp
          AND EXISTS (
              SELECT 1
              FROM fn_identity_refresh_session AS session
              WHERE session.Id = @SessionId
                AND session.UserId = fn_identity_user.Id
                AND session.ExpiresAtUtc > @NowUtc
                AND session.ConsumedAtUtc IS NULL
                AND session.RevokedAtUtc IS NULL
          )
        """,
        SqlDataScope.Global);

}

internal sealed record ConsumeRefreshSessionUpdate(
    Guid Id,
    DateTimeOffset ConsumedAtUtc,
    Guid ReplacedById,
    int Version);

internal sealed record LoginFailureUpdate(
    Guid Id,
    int FailedLoginCount,
    DateTimeOffset? LockoutEndUtc,
    DateTimeOffset UpdatedAtUtc,
    int Version);

internal sealed record LoginSuccessUpdate(
    Guid Id,
    string PasswordHash,
    DateTimeOffset UpdatedAtUtc,
    int Version);
