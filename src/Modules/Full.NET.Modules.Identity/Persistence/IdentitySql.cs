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
}

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
