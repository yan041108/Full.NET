using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>中心交互的局部安全状态更新；第二因子与密码 CAS 均处于 Identity 事务。</summary>
internal static class IdentityOidcInteractionSql
{
    public static readonly SqlStatement AcceptTotpCounter = new("identity.accept_oidc_totp_counter", """
        UPDATE fn_identity_user_totp SET LastAcceptedCounter = @Counter, UpdatedAtUtc = @Now, Version = Version + 1
        WHERE UserId = @UserId AND Version = @Version AND IsEnabled = 1
          AND (LastAcceptedCounter IS NULL OR LastAcceptedCounter < @Counter)
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement ChangePassword = new("identity.change_oidc_user_password", """
        UPDATE fn_identity_user
        SET PasswordHash = @PasswordHash, SecurityStamp = @SecurityStamp, MustChangePassword = 0,
            PasswordChangedAtUtc = @Now, UpdatedAtUtc = @Now, FailedLoginCount = 0, LockoutEndUtc = NULL,
            Version = Version + 1
        WHERE Id = @UserId AND ScopeKey = 'host' AND TenantId IS NULL AND Version = @Version
          AND IsActive = 1 AND SecurityStamp = @PreviousStamp
        """, SqlDataScope.HostOnly);
}
