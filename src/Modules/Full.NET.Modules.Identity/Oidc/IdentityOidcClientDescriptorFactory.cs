using Full.NET.Modules.Identity.Configuration;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Full.NET.Modules.Identity.Oidc;

internal static class IdentityOidcClientDescriptorFactory
{
    internal sealed record Input(
        string ClientId,
        string? ClientSecret,
        string DisplayName,
        IReadOnlyList<string> RedirectUris,
        IReadOnlyList<string> PostLogoutRedirectUris,
        IReadOnlyList<string> Scopes,
        bool IsFirstParty,
        string? ResourceAudience,
        bool IsDisabled = false);

    internal static OpenIddictApplicationDescriptor Build(Input input)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = input.ClientId,
            DisplayName = input.DisplayName,
            ClientType = string.IsNullOrWhiteSpace(input.ClientSecret)
                ? ClientTypes.Public
                : ClientTypes.Confidential,
        };
        if (!string.IsNullOrWhiteSpace(input.ClientSecret))
        {
            descriptor.ClientSecret = input.ClientSecret;
        }

        foreach (var redirectUri in input.RedirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(redirectUri, UriKind.Absolute));
        }

        foreach (var postLogoutRedirectUri in input.PostLogoutRedirectUris)
        {
            descriptor.PostLogoutRedirectUris.Add(new Uri(postLogoutRedirectUri, UriKind.Absolute));
        }

        descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        descriptor.Permissions.Add(Permissions.Endpoints.Authorization);
        descriptor.Permissions.Add(Permissions.Endpoints.Token);
        descriptor.Permissions.Add(Permissions.Endpoints.EndSession);
        descriptor.Permissions.Add(Permissions.GrantTypes.AuthorizationCode);
        descriptor.Permissions.Add(Permissions.ResponseTypes.Code);
        var allowedScopes = new HashSet<string>(
            input.Scopes.Count > 0 ? input.Scopes : ["openid", "profile"],
            StringComparer.OrdinalIgnoreCase);
        allowedScopes.Add(Scopes.OpenId);
        allowedScopes.Add(Scopes.Profile);
        allowedScopes.Add(Scopes.OfflineAccess);
        foreach (var scope in allowedScopes)
        {
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
        }

        descriptor.Permissions.Add(Permissions.GrantTypes.RefreshToken);
        IdentityOidcClientMetadata.Write(
            descriptor,
            input.IsFirstParty,
            input.ResourceAudience,
            input.IsDisabled);
        return descriptor;
    }

    internal static OpenIddictApplicationDescriptor BuildFromOptions(IdentityOidcClientOptions client) =>
        Build(new Input(
            client.ClientId,
            client.ClientSecret,
            client.ClientId,
            client.RedirectUris,
            client.PostLogoutRedirectUris,
            client.Scopes.Length > 0 ? client.Scopes : ["openid", "profile"],
            client.IsFirstParty,
            string.IsNullOrWhiteSpace(client.ResourceAudience) ? null : client.ResourceAudience));
}