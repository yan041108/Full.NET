using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Configuration;

internal sealed class IdentityOidcOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<IdentityOidcOptions>
{
    public ValidateOptionsResult Validate(string? name, IdentityOidcOptions options)
    {
        if (!options.Enable)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Issuer)
            || !Uri.TryCreate(options.Issuer, UriKind.Absolute, out var issuerUri)
            || (issuerUri.Scheme != Uri.UriSchemeHttp && issuerUri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(issuerUri.UserInfo))
        {
            failures.Add("Identity:Oidc Issuer must be an absolute http(s) URI when OIDC is enabled.");
        }

        if (options.SigningKeys is null)
        {
            failures.Add("Identity:Oidc SigningKeys configuration is required when OIDC is enabled.");
        }
        else if (options.SigningKeys.Values.Any(value => value is null))
        {
            failures.Add("Identity:Oidc SigningKeys must not contain null entries.");
        }

        var supportsEphemeralSigning = environment.IsDevelopment()
            || environment.IsEnvironment("Testing");
        if (options.AllowDevelopmentEphemeralSigningKey && !supportsEphemeralSigning)
        {
            failures.Add(
                "Identity:Oidc ephemeral signing keys are allowed only in Development or Testing.");
        }

        if (!(options.AllowDevelopmentEphemeralSigningKey && supportsEphemeralSigning)
            && !HasConfiguredActiveSigningKey(options))
        {
            failures.Add(
                "Identity:Oidc requires a production signing key and matching ActiveSigningKeyId when enabled.");
        }

        if (options.Clients is null || options.Clients.Length == 0)
        {
            failures.Add("Identity:Oidc requires at least one registered client when enabled.");
        }
        else
        {
            ValidateClients(options.Clients, failures);
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateClients(
        IdentityOidcClientOptions[] clients,
        List<string> failures)
    {
        var seenClientIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var client in clients)
        {
            if (client is null)
            {
                failures.Add("Identity:Oidc client entries must not be null.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(client.ClientId))
            {
                failures.Add("Identity:Oidc client_id is required.");
            }
            else if (!seenClientIds.Add(client.ClientId))
            {
                failures.Add($"Identity:Oidc client_id '{client.ClientId}' is duplicated.");
            }

            if (client.RedirectUris is null || client.RedirectUris.Length == 0)
            {
                failures.Add(
                    $"Identity:Oidc client '{client.ClientId}' requires at least one redirect URI.");
            }
            else
            {
                ValidateRedirectUris(
                    client.ClientId,
                    client.RedirectUris,
                    "redirect",
                    failures);
            }

            if (client.PostLogoutRedirectUris is not null)
            {
                ValidateRedirectUris(
                    client.ClientId,
                    client.PostLogoutRedirectUris,
                    "post-logout redirect",
                    failures);
            }
        }
    }

    private static void ValidateRedirectUris(
        string clientId,
        string[] redirectUris,
        string kind,
        List<string> failures)
    {
        var seenUris = new HashSet<string>(StringComparer.Ordinal);
        foreach (var redirectUri in redirectUris)
        {
            if (!IdentityOidcRedirectUriPolicy.IsExactAbsoluteUri(redirectUri))
            {
                failures.Add(
                    $"Identity:Oidc client '{clientId}' has an invalid {kind} URI '{redirectUri}'.");
                continue;
            }

            var normalized = redirectUri.TrimEnd('/');
            if (!seenUris.Add(normalized))
            {
                failures.Add(
                    $"Identity:Oidc client '{clientId}' duplicates {kind} URI '{redirectUri}'.");
            }
        }
    }

    private static bool HasConfiguredActiveSigningKey(IdentityOidcOptions options) =>
        !string.IsNullOrWhiteSpace(options.ActiveSigningKeyId)
        && options.SigningKeys is not null
        && options.SigningKeys.TryGetValue(options.ActiveSigningKeyId, out var key)
        && key is not null
        && !string.IsNullOrWhiteSpace(key.PublicKeyPem)
        && !string.IsNullOrWhiteSpace(key.PrivateKeyPem);
}