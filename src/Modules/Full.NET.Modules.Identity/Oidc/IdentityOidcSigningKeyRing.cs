using System.Security.Cryptography;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>
/// OIDC 协议专用 RSA 签名环，与 Identity JWT 签名环分离以支持独立轮转。
/// T03 采用<strong>签名 JWT</strong>（非 JWE）承载 Access Token，并写入 <c>fn:token_use=access</c>；
/// 资源 API 通过既有 JwtBearer + <see cref="IdentityOidcAccessSessionValidator"/> 路由验签与会话权威校验。
/// </summary>
internal sealed class IdentityOidcSigningKeyRing : IDisposable
{
    private readonly object _sync = new();
    private readonly List<RSA> _ownedKeys = [];
    private readonly Dictionary<string, OidcSigningKeyEntry> _entries = new(StringComparer.Ordinal);
    private string _activeKeyId;

    public IdentityOidcSigningKeyRing(
        IOptions<IdentityOidcOptions> options,
        ILogger<IdentityOidcSigningKeyRing> logger)
    {
        var settings = options.Value;
        if (!settings.Enable)
        {
            throw new InvalidOperationException(
                "IdentityOidcSigningKeyRing requires Identity:Oidc:Enable=true.");
        }

        UsesEphemeralDevelopmentKey =
            settings.SigningKeys.Count == 0 && settings.AllowDevelopmentEphemeralSigningKey;
        if (UsesEphemeralDevelopmentKey)
        {
            if (!settings.AllowDevelopmentEphemeralSigningKey)
            {
                throw new InvalidOperationException(
                    "OIDC signing keys are not configured.");
            }

            var rsa = RSA.Create(3072);
            _ownedKeys.Add(rsa);
            var securityKey = new RsaSecurityKey(rsa)
            {
                KeyId = $"oidc-dev-{Guid.NewGuid():N}",
            };
            _activeKeyId = securityKey.KeyId!;
            _entries[_activeKeyId] = new OidcSigningKeyEntry(securityKey, HasPrivateKey: true);
            logger.LogWarning(
                "OIDC is using an ephemeral development signing key with KeyId {KeyId}",
                securityKey.KeyId);
            return;
        }

        try
        {
            foreach (var pair in settings.SigningKeys)
            {
                var rsa = RSA.Create();
                _ownedKeys.Add(rsa);
                var configuredKey = pair.Value
                    ?? throw new InvalidOperationException(
                        $"OIDC signing key '{pair.Key}' configuration is missing.");
                if (!string.IsNullOrWhiteSpace(configuredKey.PrivateKeyPem))
                {
                    rsa.ImportFromPem(NormalizePem(configuredKey.PrivateKeyPem));
                }
                else
                {
                    rsa.ImportFromPem(NormalizePem(configuredKey.PublicKeyPem));
                }

                if (rsa.KeySize < 2048)
                {
                    throw new InvalidOperationException(
                        $"OIDC signing key '{pair.Key}' must be at least 2048 bits.");
                }

                var securityKey = new RsaSecurityKey(rsa) { KeyId = pair.Key };
                _entries[pair.Key] = new OidcSigningKeyEntry(
                    securityKey,
                    HasPrivateKey: !string.IsNullOrWhiteSpace(configuredKey.PrivateKeyPem));
            }
        }
        catch
        {
            Dispose();
            throw;
        }

        _activeKeyId = settings.ActiveSigningKeyId;
        if (!_entries.TryGetValue(_activeKeyId, out var activeEntry)
            || !activeEntry.HasPrivateKey)
        {
            throw new InvalidOperationException(
                "OIDC ActiveSigningKeyId does not identify a configured private signing key.");
        }
    }

    public bool UsesEphemeralDevelopmentKey { get; }

    public string ActiveSigningKeyId
    {
        get
        {
            lock (_sync)
            {
                return _activeKeyId;
            }
        }
    }

    public SigningCredentials SigningCredentials
    {
        get
        {
            lock (_sync)
            {
                return CreateSigningCredentials(_entries[_activeKeyId].SecurityKey);
            }
        }
    }

    public IReadOnlyCollection<SecurityKey> ValidationKeys
    {
        get
        {
            lock (_sync)
            {
                return _entries.Values
                    .Select(entry => entry.SecurityKey)
                    .ToArray();
            }
        }
    }

    public IReadOnlyDictionary<string, OidcSigningKeyEntry> Entries
    {
        get
        {
            lock (_sync)
            {
                return new Dictionary<string, OidcSigningKeyEntry>(_entries, StringComparer.Ordinal);
            }
        }
    }

    public Result<string> TryActivate(string keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        if (UsesEphemeralDevelopmentKey)
        {
            return Result<string>.Failure(new Error(
                ValidationErrorCodes.Failed,
                "Ephemeral development signing keys cannot be activated through the management API.",
                ErrorType.Validation));
        }

        lock (_sync)
        {
            if (string.Equals(_activeKeyId, keyId, StringComparison.Ordinal))
            {
                return Result<string>.Success(_activeKeyId);
            }

            if (!_entries.TryGetValue(keyId, out var entry) || !entry.HasPrivateKey)
            {
                return Result<string>.Failure(new Error(
                    IdentityErrorCodes.OidcSigningKeyNotActivatable,
                    "The OIDC signing key is missing, unknown, or does not include private key material.",
                    ErrorType.Validation));
            }

            _activeKeyId = keyId;
            return Result<string>.Success(_activeKeyId);
        }
    }

    public void Dispose()
    {
        foreach (var key in _ownedKeys)
        {
            key.Dispose();
        }

        _ownedKeys.Clear();
        _entries.Clear();
    }

    private static SigningCredentials CreateSigningCredentials(SecurityKey securityKey) =>
        new(securityKey, SecurityAlgorithms.RsaSha256);

    private static ReadOnlySpan<char> NormalizePem(string pem) =>
        pem.Replace("\\n", "\n", StringComparison.Ordinal).AsSpan();
}

internal sealed record OidcSigningKeyEntry(SecurityKey SecurityKey, bool HasPrivateKey);