using System.Security.Cryptography;
using Full.NET.Modules.Identity.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Full.NET.Modules.Identity.Security;

/// <summary>
/// RSA 多密钥签名环。维护一份激活的 SigningCredentials（含私钥，用于签发）
/// 与多份 ValidationKeys（仅公钥，用于验签），支持签发密钥平滑轮转：
/// 在 ActiveKeyId 指向的新密钥签发成功后，旧公钥仍保留在 ValidationKeys 中，
/// 直到现存旧 Access Token 全部过期。最小密钥长度 2048-bit；Development 未配置时
/// 可允许生成 3072-bit 临时密钥但 Production 必须禁用。
/// </summary>
internal sealed class RsaSigningKeyRing : IDisposable
{
    private readonly List<RSA> _ownedKeys = [];

    public RsaSigningKeyRing(
        IOptions<IdentityOptions> options,
        ILogger<RsaSigningKeyRing> logger)
    {
        var settings = options.Value;
        if (settings.SigningKeys.Count == 0)
        {
            if (!settings.AllowDevelopmentEphemeralSigningKey)
            {
                throw new InvalidOperationException(
                    "Identity signing keys are not configured.");
            }

            var rsa = RSA.Create(3072);
            _ownedKeys.Add(rsa);
            var securityKey = new RsaSecurityKey(rsa)
            {
                KeyId = $"dev-{Guid.NewGuid():N}",
            };
            SigningCredentials = new SigningCredentials(
                securityKey,
                SecurityAlgorithms.RsaSha256);
            ValidationKeys = [securityKey];
            logger.LogWarning(
                "Identity is using an ephemeral development signing key with KeyId {KeyId}",
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
                    settings.ActiveKeyId,
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
                        $"Identity signing key '{pair.Key}' must be at least 2048 bits.");
                }

                var securityKey = new RsaSecurityKey(rsa) { KeyId = pair.Key };
                validationKeys.Add(securityKey);
                if (string.Equals(
                    pair.Key,
                    settings.ActiveKeyId,
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
            "Identity ActiveKeyId does not identify a configured private signing key.");
        ValidationKeys = validationKeys;
    }

    /// <summary>当前激活签名凭据（含私钥），每次签发 JWT 时使用。</summary>
    public SigningCredentials SigningCredentials { get; }

    /// <summary>参与 JWT 验签的所有公钥集合，至少包含当前激活 Key，用于支持多密钥平滑轮转。</summary>
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
