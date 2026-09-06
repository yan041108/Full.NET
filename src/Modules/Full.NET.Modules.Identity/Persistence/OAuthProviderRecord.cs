namespace Full.NET.Modules.Identity.Persistence;

/// <summary>映射 <c>fn_identity_oauth_provider</c> 行。</summary>
internal sealed class OAuthProviderRecord
{
    public Guid Id { get; set; }

    public string ProviderKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Authority { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecretProtected { get; set; } = string.Empty;

    public string Scopes { get; set; } = string.Empty;

    public string RedirectPath { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public int Version { get; set; }
}
