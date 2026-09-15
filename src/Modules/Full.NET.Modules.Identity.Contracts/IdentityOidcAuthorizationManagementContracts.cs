namespace Full.NET.Modules.Identity.Contracts;

/// <summary>OIDC 授权授予管理 API 契约。</summary>
public static class IdentityOidcAuthorizationPermissions
{
    public const string Read = "identity.oidc_authorizations.read";
    public const string Revoke = "identity.oidc_authorizations.revoke";
}

public sealed record OidcAuthorizationResponse(
    Guid Id,
    Guid? ApplicationId,
    string? ClientId,
    string? Subject,
    IReadOnlyList<string> Scopes,
    string Status,
    string Type,
    DateTimeOffset? CreationDateUtc,
    DateTimeOffset CreatedAtUtc,
    int Version);