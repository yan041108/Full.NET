using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

internal static class IdentitySql
{
    public static readonly SqlStatement FindUserByScopeAndUsername = new(
        "identity.find-user-by-scope-and-username",
        """
        SELECT Id, TenantId, ScopeKey, Username, NormalizedUsername, DisplayName,
               PasswordHash, IsActive, FailedLoginCount, LockoutEndUtc,
               SecurityStamp, CreatedAtUtc, UpdatedAtUtc, Version,
               PreferredLocale, ProfileVersion
        FROM fn_identity_user
        WHERE ScopeKey = @ScopeKey AND NormalizedUsername = @NormalizedUsername
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertUser = new(
        "identity.insert-user",
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
        "identity.find-refresh-session-by-explicit-session-id",
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
        "identity.update-refresh-session-explicit-context",
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
        "identity.insert-explicit-context-audit",
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
        "identity.find-role-by-scope-and-code",
        """
        SELECT Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
               CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_identity_role
        WHERE ScopeKey = @ScopeKey AND Code = @Code
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertRole = new(
        "identity.insert-role",
        """
        INSERT INTO fn_identity_role
            (Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive,
             CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @ScopeKey, @Code, @Name, @IsSystem, @IsActive,
             @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateSystemRole = new(
        "identity.update-system-role",
        """
        UPDATE fn_identity_role
        SET Name = @Name,
            IsSystem = 1,
            IsActive = 1,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement GetRolePermissionCodes = new(
        "identity.get-role-permission-codes",
        """
        SELECT PermissionCode
        FROM fn_identity_role_permission
        WHERE RoleId = @RoleId
        ORDER BY PermissionCode
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement EnsureRolePermission = new(
        "identity.ensure-role-permission",
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
        "identity.ensure-user-role",
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

    public static readonly SqlStatement GetUserPermissionCodes = new(
        "identity.get-explicit-actor-permission-codes",
        """
        SELECT rolePermission.PermissionCode
        FROM fn_identity_user_role AS userRole
        INNER JOIN fn_identity_role AS roleObject ON roleObject.Id = userRole.RoleId
        INNER JOIN fn_identity_role_permission AS rolePermission
            ON rolePermission.RoleId = roleObject.Id
        WHERE userRole.UserId = @UserId
          AND roleObject.IsActive = 1
          AND roleObject.ScopeKey = @ScopeKey
          AND
          (
              (roleObject.TenantId IS NULL AND @TenantId IS NULL)
              OR roleObject.TenantId = @TenantId
          )
        ORDER BY rolePermission.PermissionCode
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement UpdateLoginFailure = new(
        "identity.update-login-failure",
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
        "identity.update-login-success",
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
        "identity.insert-refresh-session",
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
        "identity.insert-auth-audit",
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
        "identity.count-authentication-audits",
        "SELECT COUNT(*) FROM fn_identity_auth_audit",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindRefreshSessionByHash = new(
        "identity.find-refresh-session-by-hash",
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
        "identity.consume-refresh-session",
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
        "identity.revoke-refresh-family",
        """
        UPDATE fn_identity_refresh_session
        SET RevokedAtUtc = @RevokedAtUtc,
            Version = Version + 1
        WHERE FamilyId = @FamilyId AND RevokedAtUtc IS NULL
        """,
        SqlDataScope.HostOnly);

    // Global 查询仅接受 JWT 验证后的 sub 与演员原始作用域，两项必须同时命中。
    public static readonly SqlStatement FindProfileByIdentity = new(
        "identity.find-profile-by-verified-identity",
        """
        SELECT Id, ScopeKey, Username, DisplayName, IsActive,
               PreferredLocale, ProfileVersion
        FROM fn_identity_user
        WHERE Id = @UserId AND ScopeKey = @ScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement UpdateLocalePreference = new(
        "identity.update-locale-preference-by-verified-identity",
        """
        UPDATE fn_identity_user
        SET PreferredLocale = @PreferredLocale,
            ProfileVersion = ProfileVersion + 1
        WHERE Id = @UserId
          AND ScopeKey = @ScopeKey
          AND ProfileVersion = @ProfileVersion
          AND IsActive = 1
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
