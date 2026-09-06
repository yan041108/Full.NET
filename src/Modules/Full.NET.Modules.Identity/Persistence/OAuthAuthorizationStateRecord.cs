namespace Full.NET.Modules.Identity.Persistence;

/// <summary>映射 <c>fn_identity_oauth_authorization_state</c> 行。</summary>
internal sealed class OAuthAuthorizationStateRecord
{
    public Guid Id { get; set; }

    public string ProviderKey { get; set; } = string.Empty;

    public string CodeVerifier { get; set; } = string.Empty;

    public string Nonce { get; set; } = string.Empty;

    public string Mode { get; set; } = string.Empty;

    public Guid? UserId { get; set; }

    public string ReturnUrl { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }
}
