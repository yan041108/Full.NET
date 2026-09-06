using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Payments.Domain;

/// <summary>支付宝 OpenAPI RSA2 签名辅助。</summary>
internal static class AlipaySigner
{
    /// <summary>按字典序拼接待签名字符串（key=value&amp;...）。</summary>
    /// <param name="parameters">请求参数字典；不含 sign 与空值。</param>
    /// <returns>待签名字符串。</returns>
    public static string BuildSignContent(IReadOnlyDictionary<string, string> parameters)
    {
        var builder = new StringBuilder();
        foreach (var pair in parameters.OrderBy(static entry => entry.Key, StringComparer.Ordinal))
        {
            if (string.Equals(pair.Key, "sign", StringComparison.Ordinal)
                || string.IsNullOrEmpty(pair.Value))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('&');
            }

            builder.Append(pair.Key);
            builder.Append('=');
            builder.Append(pair.Value);
        }

        return builder.ToString();
    }

    /// <summary>使用商户私钥对待签名字符串进行 RSA2（SHA256）签名。</summary>
    /// <param name="signContent">待签名字符串。</param>
    /// <param name="privateKeyPem">商户私钥 PEM。</param>
    /// <returns>Base64 编码签名。</returns>
    public static string Sign(string signContent, string privateKeyPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var signature = rsa.SignData(
            Encoding.UTF8.GetBytes(signContent),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(signature);
    }
}
