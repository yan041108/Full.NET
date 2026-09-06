namespace Full.NET.Modules.Identity.Persistence;

/// <summary>映射 <c>fn_identity_oauth_user_link</c> 行。</summary>
internal sealed class OAuthUserLinkRecord
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string ProviderKey { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string? Email { get; set; }

    public bool EmailVerified { get; set; }

    public string? DisplayName { get; set; }

    public DateTimeOffset LinkedAtUtc { get; set; }

    public DateTimeOffset? LastUsedAtUtc { get; set; }

    public int Version { get; set; }
}
