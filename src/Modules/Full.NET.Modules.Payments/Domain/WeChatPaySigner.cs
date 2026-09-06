using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Payments.Domain;

/// <summary>微信支付 API v3 RSA-SHA256 签名辅助。</summary>
internal static class WeChatPaySigner
{
    /// <summary>构建待签名的规范消息。</summary>
    /// <param name="method">HTTP 方法。</param>
    /// <param name="urlPathWithQuery">URL 路径与查询字符串。</param>
    /// <param name="timestamp">Unix 时间戳（秒）。</param>
    /// <param name="nonce">随机字符串。</param>
    /// <param name="body">请求体；GET 等无正文请求传空字符串。</param>
    /// <returns>规范消息文本。</returns>
    public static string BuildCanonicalMessage(
        string method,
        string urlPathWithQuery,
        long timestamp,
        string nonce,
        string body) =>
        string.Join(
            '\n',
            method,
            urlPathWithQuery,
            timestamp.ToString(),
            nonce,
            body) + '\n';

    /// <summary>使用商户私钥对规范消息进行 RSA-SHA256 签名。</summary>
    /// <param name="canonicalMessage">规范消息。</param>
    /// <param name="privateKeyPem">商户私钥 PEM。</param>
    /// <returns>Base64 编码签名。</returns>
    public static string Sign(string canonicalMessage, string privateKeyPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var signature = rsa.SignData(
            Encoding.UTF8.GetBytes(canonicalMessage),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(signature);
    }

    /// <summary>构建微信支付 Authorization 请求头值。</summary>
    /// <param name="merchantId">商户号。</param>
    /// <param name="certificateSerialNo">证书序列号。</param>
    /// <param name="timestamp">Unix 时间戳（秒）。</param>
    /// <param name="nonce">随机字符串。</param>
    /// <param name="signature">Base64 签名。</param>
    /// <returns>Authorization 头值。</returns>
    public static string BuildAuthorizationHeader(
        string merchantId,
        string certificateSerialNo,
        long timestamp,
        string nonce,
        string signature) =>
        $"WECHATPAY2-SHA256-RSA2048 mchid=\"{merchantId}\",nonce_str=\"{nonce}\",timestamp=\"{timestamp}\",serial_no=\"{certificateSerialNo}\",signature=\"{signature}\"";
}
