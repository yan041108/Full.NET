using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Identity.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>用规范公钥 DER 指纹绑定 kid，拒绝同名不同材料；共享材料仅暴露高熵密钥的摘要。</summary>
internal static class IdentityOidcKeyMaterial
{
    internal static bool IsValidId(string? id) => !string.IsNullOrEmpty(id) && id.Length <= 128 && char.IsAsciiLetterOrDigit(id[0])
        && id.All(value => char.IsAsciiLetterOrDigit(value) || value is '-' or '_' or '.');
    internal static string Hash(SecurityKey key) => key is RsaSecurityKey { Rsa: not null } rsa
        ? Convert.ToHexString(SHA256.HashData(rsa.Rsa.ExportSubjectPublicKeyInfo()))
        : throw new InvalidOperationException("The signing key must contain RSA public material.");
    internal static string Hash(IdentityOidcSigningKeyOptions options)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem((string.IsNullOrEmpty(options.PublicKeyPem) ? options.PrivateKeyPem : options.PublicKeyPem).Replace("\\n", "\n", StringComparison.Ordinal));
        if (rsa.KeySize < 2048) throw new InvalidOperationException("Signing keys require at least 2048 bits.");
        var expected = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()));
        if (!string.IsNullOrEmpty(options.PrivateKeyPem))
        {
            rsa.ImportFromPem(options.PrivateKeyPem.Replace("\\n", "\n", StringComparison.Ordinal));
            if (expected != Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())))
                throw new InvalidOperationException("Configured signing public and private material do not match.");
        }
        return expected;
    }
    internal static string Manifest(IdentityOidcSigningKeyRing keys) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join('|', keys.Entries.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + ":" + Hash(pair.Value.SecurityKey))))));
    internal static string Encryption(string key) => Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(key)));
}
