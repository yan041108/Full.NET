using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Payments.Domain;

/// <summary>微信支付 API v3 通知签名验签。</summary>
internal static class WeChatPaySignatureVerifier
{
    /// <summary>使用平台公钥验证通知签名。</summary>
    /// <param name="publicKeyPem">平台证书公钥 PEM。</param>
    /// <param name="timestamp">Wechatpay-Timestamp 头。</param>
    /// <param name="nonce">Wechatpay-Nonce 头。</param>
    /// <param name="body">原始请求体。</param>
    /// <param name="signatureBase64">Wechatpay-Signature 头。</param>
    /// <param name="requestPath">接收路径；保留调用兼容性，但通知签名不包含路径。</param>
    /// <returns>验签是否通过。</returns>
    public static bool Verify(
        string publicKeyPem,
        string timestamp,
        string nonce,
        string body,
        string signatureBase64,
        string requestPath)
    {
        if (!long.TryParse(timestamp, out _))
        {
            return false;
        }

        // 微信通知与商户出站请求采用不同规范串；必须保留收到的时间戳和原始正文。
        var canonicalMessage = $"{timestamp}\n{nonce}\n{body}\n";
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            return rsa.VerifyData(
                Encoding.UTF8.GetBytes(canonicalMessage),
                Convert.FromBase64String(signatureBase64),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
