namespace Full.NET.Modules.Identity.Contracts;

/// <summary>OIDC 客户端管理 API 契约。</summary>
public static class IdentityOidcClientPermissions
{
    public const string Read = "identity.oidc_clients.read";
    public const string Create = "identity.oidc_clients.create";
    public const string Update = "identity.oidc_clients.update";
    public const string Disable = "identity.oidc_clients.disable";
    public const string Rotate = "identity.oidc_clients.rotate";
}

public sealed record CreateOidcClientRequest(
    string ClientId,
    string DisplayName,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string>? PostLogoutRedirectUris,
    IReadOnlyList<string> Scopes,
    bool IsConfidential,
    bool IsFirstParty,
    string? ResourceAudience);

public sealed record UpdateOidcClientRequest(
    string DisplayName,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string>? PostLogoutRedirectUris,
    IReadOnlyList<string> Scopes,
    bool IsFirstParty,
    string? ResourceAudience,
    int Version);

public sealed record OidcClientResponse(
    Guid Id,
    string ClientId,
    string DisplayName,
    string ClientType,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> PostLogoutRedirectUris,
    IReadOnlyList<string> Scopes,
    bool IsFirstParty,
    string? ResourceAudience,
    bool IsDisabled,
    DateTimeOffset CreatedAtUtc,
    int Version);

public sealed record CreateOidcClientResponse(
    OidcClientResponse Client,
    string? Secret);

public sealed record RotateOidcClientSecretResponse(
    OidcClientResponse Client,
    string Secret);