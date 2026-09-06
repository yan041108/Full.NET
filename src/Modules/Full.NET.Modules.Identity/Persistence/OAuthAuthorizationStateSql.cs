using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>OAuth 授权状态临时存储 SQL。</summary>
internal static class OAuthAuthorizationStateSql
{
    public static readonly SqlStatement Insert = new(
        "identity.insert_oauth_authorization_state",
        """
        INSERT INTO fn_identity_oauth_authorization_state
            (Id, ProviderKey, CodeVerifier, Nonce, Mode, UserId, ReturnUrl, CreatedAtUtc, ExpiresAtUtc)
        VALUES
            (@Id, @ProviderKey, @CodeVerifier, @Nonce, @Mode, @UserId, @ReturnUrl, @CreatedAtUtc, @ExpiresAtUtc)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "identity.find_oauth_authorization_state_by_id",
        """
        SELECT state.Id,
               state.ProviderKey,
               state.CodeVerifier,
               state.Nonce,
               state.Mode,
               state.UserId,
               state.ReturnUrl,
               state.CreatedAtUtc,
               state.ExpiresAtUtc
        FROM fn_identity_oauth_authorization_state AS state
        WHERE state.Id = @StateId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteById = new(
        "identity.delete_oauth_authorization_state_by_id",
        """
        DELETE FROM fn_identity_oauth_authorization_state
        WHERE Id = @StateId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteExpired = new(
        "identity.delete_expired_oauth_authorization_states",
        """
        DELETE FROM fn_identity_oauth_authorization_state
        WHERE ExpiresAtUtc <= @NowUtc
        """,
        SqlDataScope.HostOnly);
}
