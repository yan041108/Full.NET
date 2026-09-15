using System.Security.Cryptography;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Full.NET.Modules.Identity.Features.ManageOidcSigningKeys;

/// <summary>读取当前 OIDC 签名密钥环元数据；响应仅含公钥，供运维核对 JWKS 与轮转窗口。</summary>
internal sealed class OidcSigningKeyQueryService(
    IdentityOidcSigningKeyRing keyRing,
    IOptions<IdentityOidcOptions> options)
{
    public Result<OidcSigningKeyListResponse> List()
    {
        var settings = options.Value;
        var usesEphemeralDevelopmentKey =
            settings.SigningKeys.Count == 0 && settings.AllowDevelopmentEphemeralSigningKey;
        var activeSigningKeyId = usesEphemeralDevelopmentKey
            ? keyRing.SigningCredentials.Key.KeyId ?? string.Empty
            : settings.ActiveSigningKeyId;
        var keys = keyRing.ValidationKeys
            .Select(securityKey => MapKey(securityKey, activeSigningKeyId, settings, usesEphemeralDevelopmentKey))
            .OrderBy(key => key.KeyId, StringComparer.Ordinal)
            .ToArray();
        return Result<OidcSigningKeyListResponse>.Success(
            new OidcSigningKeyListResponse(
                activeSigningKeyId,
                usesEphemeralDevelopmentKey,
                keys));
    }

    private static OidcSigningKeyResponse MapKey(
        SecurityKey securityKey,
        string activeSigningKeyId,
        IdentityOidcOptions settings,
        bool usesEphemeralDevelopmentKey)
    {
        var keyId = securityKey.KeyId ?? string.Empty;
        var hasPrivateKey = usesEphemeralDevelopmentKey
            ? string.Equals(keyId, activeSigningKeyId, StringComparison.Ordinal)
            : settings.SigningKeys.TryGetValue(keyId, out var configuredKey)
                && !string.IsNullOrWhiteSpace(configuredKey.PrivateKeyPem);
        return new OidcSigningKeyResponse(
            keyId,
            string.Equals(keyId, activeSigningKeyId, StringComparison.Ordinal),
            hasPrivateKey,
            SecurityAlgorithms.RsaSha256,
            ExportPublicKeyPem(securityKey));
    }

    private static string ExportPublicKeyPem(SecurityKey securityKey)
    {
        if (securityKey is not RsaSecurityKey rsaSecurityKey)
        {
            throw new InvalidOperationException(
                $"OIDC signing key '{securityKey.KeyId}' is not an RSA security key.");
        }

        var rsa = rsaSecurityKey.Rsa ?? RSA.Create();
        try
        {
            return rsa.ExportRSAPublicKeyPem();
        }
        finally
        {
            if (rsaSecurityKey.Rsa is null)
            {
                rsa.Dispose();
            }
        }
    }
}