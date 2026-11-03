using System.Security.Cryptography;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.IdentityModel.Tokens;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.ManageOidcSigningKeys;

/// <summary>读取当前 OIDC 签名密钥环元数据；响应仅含公钥，供运维核对 JWKS 与轮转窗口。</summary>
internal sealed class OidcSigningKeyQueryService(IdentityOidcSigningKeyRing keyRing, IdentityOidcSigningPolicy policy,
    IdentityOidcKeyGovernance governance, IClock clock)
{
    public async Task<Result<OidcSigningKeyListResponse>> ListAsync(CancellationToken cancellationToken)
    {
        await policy.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
        var usesEphemeralDevelopmentKey = keyRing.UsesEphemeralDevelopmentKey;
        var activeSigningKeyId = keyRing.ActiveSigningKeyId;
        var entries = keyRing.Entries;
        IReadOnlyList<IdentityOidcSigningKeyRow> lifecycle = usesEphemeralDevelopmentKey ? [] : await governance.ReadKeysAsync(cancellationToken).ConfigureAwait(false);
        var readiness = await governance.ActivationReadinessAsync(cancellationToken).ConfigureAwait(false);
        var keys = entries.Values
            .Select(entry => MapKey(
                entry.SecurityKey,
                activeSigningKeyId,
                entries,
                usesEphemeralDevelopmentKey,
                lifecycle.SingleOrDefault(row => row.KeyId == entry.SecurityKey.KeyId), readiness.Keys, clock.UtcNow))
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
        bool usesEphemeralDevelopmentKey,
        IdentityOidcSigningKeyRow? lifecycle,
        IReadOnlyDictionary<string, bool> readiness,
        DateTimeOffset now)
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
            ExportPublicKeyPem(securityKey),
            lifecycle?.IsRetired == true ? "retired" : keyId == activeSigningKeyId ? "active"
                : lifecycle is null ? "unregistered" : lifecycle.LastActivatedAtUtc.HasValue ? "retained" : "prepublished",
            IdentityOidcKeyMaterial.Hash(securityKey), lifecycle?.PublishedAtUtc, lifecycle?.RetireAfterUtc,
            keyId != activeSigningKeyId && readiness.GetValueOrDefault(keyId),
            lifecycle is { IsRetired: false } && keyId != activeSigningKeyId
                && (!lifecycle.LastActivatedAtUtc.HasValue || lifecycle.RetireAfterUtc <= now));
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
