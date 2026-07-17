using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

internal static class IdentitySql
{
    public static readonly SqlStatement FindUserByScopeAndUsername = new(
        "identity.find-user-by-scope-and-username",
        """
        SELECT Id, TenantId, ScopeKey, Username, NormalizedUsername, DisplayName,
               PasswordHash, IsActive, FailedLoginCount, LockoutEndUtc,
               SecurityStamp, CreatedAtUtc, UpdatedAtUtc, Version
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
             SecurityStamp, CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @ScopeKey, @Username, @NormalizedUsername, @DisplayName,
             @PasswordHash, @IsActive, @FailedLoginCount, @LockoutEndUtc,
             @SecurityStamp, @CreatedAtUtc, @UpdatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

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
             ConsumedAtUtc, RevokedAtUtc, ReplacedById, CreatedAtUtc, Version)
        VALUES
            (@Id, @UserId, @FamilyId, @ClientId, @TokenHash, @ExpiresAtUtc,
             @ConsumedAtUtc, @RevokedAtUtc, @ReplacedById, @CreatedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertAuthAudit = new(
        "identity.insert-auth-audit",
        """
        INSERT INTO fn_identity_auth_audit
            (Id, UserId, SessionId, UsernameFingerprint, EventType,
             ResultCode, Succeeded, IpAddress, UserAgent, OccurredAtUtc)
        VALUES
            (@Id, @UserId, @SessionId, @UsernameFingerprint, @EventType,
             @ResultCode, @Succeeded, @IpAddress, @UserAgent, @OccurredAtUtc)
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
               identityUser.Version AS UserVersion
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

    public static readonly SqlStatement RevokeRefreshSession = new(
        "identity.revoke-refresh-session",
        """
        UPDATE fn_identity_refresh_session
        SET RevokedAtUtc = @RevokedAtUtc,
            Version = Version + 1
        WHERE Id = @Id AND RevokedAtUtc IS NULL
        """,
        SqlDataScope.HostOnly);
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
