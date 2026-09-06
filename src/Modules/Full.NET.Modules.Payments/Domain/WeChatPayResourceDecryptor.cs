using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Payments.Domain;

/// <summary>微信支付 API v3 通知资源 AES-256-GCM 解密。</summary>
internal static class WeChatPayResourceDecryptor
{
    private const int AuthTagLength = 16;

    /// <summary>使用 API v3 密钥解密通知资源。</summary>
    /// <param name="apiV3Key">32 字节 API v3 密钥明文。</param>
    /// <param name="associatedData">附加数据。</param>
    /// <param name="nonce">随机串。</param>
    /// <param name="ciphertextBase64">Base64 密文（含认证标签）。</param>
    /// <returns>解密后的 UTF-8 明文。</returns>
    public static string Decrypt(
        string apiV3Key,
        string associatedData,
        string nonce,
        string ciphertextBase64)
    {
        var keyBytes = Encoding.UTF8.GetBytes(apiV3Key);
        if (keyBytes.Length != 32)
        {
            throw new CryptographicException("WeChat API v3 key must be 32 bytes.");
        }

        var cipherBytes = Convert.FromBase64String(ciphertextBase64);
        if (cipherBytes.Length <= AuthTagLength)
        {
            throw new CryptographicException("WeChat notify ciphertext is too short.");
        }

        var ciphertext = cipherBytes[..^AuthTagLength];
        var tag = cipherBytes[^AuthTagLength..];
        var nonceBytes = Encoding.UTF8.GetBytes(nonce);
        var associatedBytes = Encoding.UTF8.GetBytes(associatedData);
        var plainBytes = new byte[ciphertext.Length];

        using var aesGcm = new AesGcm(keyBytes, AuthTagLength);
        aesGcm.Decrypt(nonceBytes, ciphertext, tag, plainBytes, associatedBytes);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
