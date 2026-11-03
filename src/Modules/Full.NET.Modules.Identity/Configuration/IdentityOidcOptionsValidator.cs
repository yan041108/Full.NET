using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Full.NET.Modules.Identity.Oidc;

/// <summary>启动期校验模块配置；非法组合在宿主启动时失败。</summary>
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
        if (options.ProtocolRateLimitPermitLimitPerMinute is < 1 or > 10000)
            failures.Add("ProtocolRateLimitPermitLimitPerMinute must be between 1 and 10000.");
        if (options.MinimumReadyApiNodes is < 1 or > 128)
            failures.Add("MinimumReadyApiNodes must be between 1 and 128.");
        if (options.SigningPrepublicationSeconds is < 0 or > 86400
            || (!environment.IsDevelopment() && !environment.IsEnvironment("Testing") && options.SigningPrepublicationSeconds < 60))
            failures.Add("SigningPrepublicationSeconds must be 0..86400 and at least 60 outside Development/Testing.");
        if (options.SigningKeys?.Keys.Any(key => !IdentityOidcKeyMaterial.IsValidId(key)) == true)
            failures.Add("Signing key identifiers must contain 1..128 ASCII letters, digits, '.', '-' or '_'.");
        if (options.BackchannelAllowedOrigins is null)
            failures.Add("Identity:Oidc BackchannelAllowedOrigins must not be null.");
        else
            foreach (var origin in options.BackchannelAllowedOrigins)
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
                    || !string.IsNullOrEmpty(uri.UserInfo) || origin != uri.GetLeftPart(UriPartial.Authority))
                    failures.Add("BackchannelAllowedOrigins must contain exact HTTPS origins without paths or credentials.");
        Uri? issuerUri = null;
        if (string.IsNullOrWhiteSpace(options.Issuer)
            || !Uri.TryCreate(options.Issuer, UriKind.Absolute, out issuerUri)
            || (issuerUri.Scheme != Uri.UriSchemeHttp && issuerUri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(issuerUri.UserInfo) || !string.IsNullOrEmpty(issuerUri.Query) || !string.IsNullOrEmpty(issuerUri.Fragment))
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
        else
        {
            foreach (var key in options.SigningKeys.Values)
                try { _ = IdentityOidcKeyMaterial.Hash(key); }
                catch (Exception error) when (error is System.Security.Cryptography.CryptographicException or ArgumentException or InvalidOperationException)
                { failures.Add("Signing key material must be matching RSA public/private material of at least 2048 bits."); }
        }

        var supportsEphemeralSigning = environment.IsDevelopment()
            || environment.IsEnvironment("Testing");
        if (!supportsEphemeralSigning && issuerUri?.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add("Identity:Oidc Issuer must use HTTPS outside Development or Testing.");
        }
        if (!supportsEphemeralSigning && string.IsNullOrWhiteSpace(options.EncryptionKeyBase64))
        {
            failures.Add("Identity:Oidc EncryptionKeyBase64 is required outside Development or Testing.");
        }
        if (options.AllowDevelopmentEphemeralSigningKey && !supportsEphemeralSigning)
        {
            failures.Add(
                "Identity:Oidc ephemeral signing keys are allowed only in Development or Testing.");
        }

        if (!(options.AllowDevelopmentEphemeralSigningKey && supportsEphemeralSigning && options.SigningKeys?.Count == 0)
            && !HasConfiguredActiveSigningKey(options))
        {
            failures.Add(
                "Identity:Oidc requires a production signing key and matching ActiveSigningKeyId when enabled.");
        }

        if (!string.IsNullOrWhiteSpace(options.EncryptionKeyBase64)
            && !TryDecodeEncryptionKey(options.EncryptionKeyBase64, out _))
        {
            failures.Add(
                "Identity:Oidc EncryptionKeyBase64 must be a valid Base64-encoded 256-bit key.");
        }

        if (options.Clients is null)
        {
            failures.Add("Identity:Oidc Clients must not be null.");
        }
        else
        {
            ValidateClients(options.Clients, failures);
            foreach (var client in options.Clients.Where(value => value is not null))
                if (!IdentityOidcBackchannelPolicy.IsAllowed(client.BackchannelLogoutUri, options.BackchannelAllowedOrigins ?? []))
                    failures.Add("A client backchannel URL is not deployment-approved.");
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

    private static bool TryDecodeEncryptionKey(string value, out byte[] keyBytes)
    {
        keyBytes = [];
        try
        {
            keyBytes = Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return false;
        }

        return keyBytes.Length == 32;
    }
}
