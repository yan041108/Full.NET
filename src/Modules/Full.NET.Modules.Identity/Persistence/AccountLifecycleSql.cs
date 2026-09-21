using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

internal static class AccountLifecycleSql
{
    public static readonly SqlStatement FindUserByProfileEmail = new(
        "identity.find_user_by_profile_email",
        """
        SELECT identityUser.Id, identityUser.TenantId, identityUser.ScopeKey, identityUser.Username,
               identityUser.NormalizedUsername, identityUser.DisplayName, identityUser.PasswordHash,
               identityUser.IsActive, identityUser.FailedLoginCount, identityUser.LockoutEndUtc,
               identityUser.SecurityStamp, identityUser.CreatedAtUtc, identityUser.UpdatedAtUtc,
               identityUser.Version, identityUser.PreferredLocale, identityUser.ProfileVersion,
               identityUser.AccountType, identityUser.MustChangePassword, identityUser.PasswordChangedAtUtc
        FROM fn_identity_user AS identityUser
        INNER JOIN fn_identity_user_profile AS profile ON profile.UserId = identityUser.Id
        WHERE profile.Email = @Email
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindUserProfileEmailByUserId = new(
        "identity.find_user_profile_email_by_user_id",
        """
        SELECT Email
        FROM fn_identity_user_profile
        WHERE UserId = @UserId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertUserProfileEmail = new(
        "identity.insert_user_profile_email",
        """
        INSERT INTO fn_identity_user_profile
            (UserId, Nickname, Email, SortOrder, Version)
        VALUES
            (@UserId, @Nickname, @Email, 100, 1)
        """,
        SqlDataScope.HostOnly);
}
