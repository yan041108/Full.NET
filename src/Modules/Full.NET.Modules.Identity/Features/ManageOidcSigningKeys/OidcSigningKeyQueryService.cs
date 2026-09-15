using System.Security.Cryptography;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.IdentityModel.Tokens;

namespace Full.NET.Modules.Identity.Features.ManageOidcSigningKeys;

/// <summary>读取当前 OIDC 签名密钥环元数据；响应仅含公钥，供运维核对 JWKS 与轮转窗口。</summary>
internal sealed class OidcSigningKeyQueryService(IdentityOidcSigningKeyRing keyRing)
{
    public Result<OidcSigningKeyListResponse> List()
    {
        var usesEphemeralDevelopmentKey = keyRing.UsesEphemeralDevelopmentKey;
        var activeSigningKeyId = keyRing.ActiveSigningKeyId;
        var entries = keyRing.Entries;
        var keys = keyRing.ValidationKeys
            .Select(securityKey => MapKey(
                securityKey,
                activeSigningKeyId,
                entries,
                usesEphemeralDevelopmentKey))
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
        IReadOnlyDictionary<string, OidcSigningKeyEntry> entries,
        bool usesEphemeralDevelopmentKey)
    {
        var keyId = securityKey.KeyId ?? string.Empty;
        var hasPrivateKey = usesEphemeralDevelopmentKey
            ? string.Equals(keyId, activeSigningKeyId, StringComparison.Ordinal)
            : entries.TryGetValue(keyId, out var entry) && entry.HasPrivateKey;
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