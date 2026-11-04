// Webhook 签名密钥保护；落库前加密，日志与 API 不返回明文。
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Webhooks.Security;

/// <summary>Webhook 签名密钥保护；落库前加密，日志与 API 不返回明文。</summary>
internal sealed class WebhookSigningSecretProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Full.NET.Webhooks.SigningSecret");
    private readonly IDataProtector _fingerprints = provider.CreateProtector("Full.NET.Webhooks.RotationFingerprint.v1");

    public string Protect(string secret) => _protector.Protect(secret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);

    // 幂等核对使用加密指纹，禁止把可离线猜测的低熵秘密摘要明文存储或返回。
    public string ProtectFingerprint(string secret) => _fingerprints.Protect(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
    public bool MatchesFingerprint(string secret, string protectedFingerprint) => CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(Encoding.UTF8.GetBytes(secret)), Convert.FromHexString(_fingerprints.Unprotect(protectedFingerprint)));
}
