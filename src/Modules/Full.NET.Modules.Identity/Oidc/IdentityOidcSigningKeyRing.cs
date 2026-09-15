using System.Security.Cryptography;
using Full.NET.Modules.Identity.Configuration;
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
    private readonly List<RSA> _ownedKeys = [];

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

        if (settings.SigningKeys.Count == 0)
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
            SigningCredentials = new SigningCredentials(
                securityKey,
                SecurityAlgorithms.RsaSha256);
            ValidationKeys = [securityKey];
            logger.LogWarning(
                "OIDC is using an ephemeral development signing key with KeyId {KeyId}",
                securityKey.KeyId);
            return;
        }

        var validationKeys = new List<SecurityKey>();
        SigningCredentials? signingCredentials = null;
        try
        {
            foreach (var pair in settings.SigningKeys)
            {
                var rsa = RSA.Create();
                _ownedKeys.Add(rsa);
                if (string.Equals(
                    pair.Key,
                    settings.ActiveSigningKeyId,
                    StringComparison.Ordinal))
                {
                    rsa.ImportFromPem(NormalizePem(pair.Value.PrivateKeyPem));
                }
                else
                {
                    rsa.ImportFromPem(NormalizePem(pair.Value.PublicKeyPem));
                }

                if (rsa.KeySize < 2048)
                {
                    throw new InvalidOperationException(
                        $"OIDC signing key '{pair.Key}' must be at least 2048 bits.");
                }

                var securityKey = new RsaSecurityKey(rsa) { KeyId = pair.Key };
                validationKeys.Add(securityKey);
                if (string.Equals(
                    pair.Key,
                    settings.ActiveSigningKeyId,
                    StringComparison.Ordinal))
                {
                    signingCredentials = new SigningCredentials(
                        securityKey,
                        SecurityAlgorithms.RsaSha256);
                }
            }
        }
        catch
        {
            Dispose();
            throw;
        }

        SigningCredentials = signingCredentials ?? throw new InvalidOperationException(
            "OIDC ActiveSigningKeyId does not identify a configured private signing key.");
        ValidationKeys = validationKeys;
    }

    public SigningCredentials SigningCredentials { get; }

    public IReadOnlyCollection<SecurityKey> ValidationKeys { get; }

    public void Dispose()
    {
        foreach (var key in _ownedKeys)
        {
            key.Dispose();
        }

        _ownedKeys.Clear();
    }

    private static ReadOnlySpan<char> NormalizePem(string pem) =>
        pem.Replace("\\n", "\n", StringComparison.Ordinal).AsSpan();
}
