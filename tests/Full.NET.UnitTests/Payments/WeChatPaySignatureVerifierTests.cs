using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Payments.Domain;

namespace Full.NET.UnitTests.Payments;

/// <summary>按微信通知协议独立构造报文，避免复用请求签名器掩盖协议差异。</summary>
[TestClass]
public sealed class WeChatPaySignatureVerifierTests
{
    /// <summary>通知签名使用时间戳、随机串和原始正文三行，不包含请求方法或路径。</summary>
    [TestMethod]
    public void Verify_accepts_signature_signed_with_platform_private_key()
    {
        using var rsa = RSA.Create(2048);
        var publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();
        const string body = """{"id":"evt-test"}""";
        const string path = "/api/v1/payments/wechat-native/notify/00000000-0000-0000-0000-000000000001";
        const string timestamp = "1556208000";
        const string nonce = "593BEC0C930BF1AFEB40B4A08C8FB242";
        var canonicalMessage = $"{timestamp}\n{nonce}\n{body}\n";
        var signature = Convert.ToBase64String(
            rsa.SignData(
                Encoding.UTF8.GetBytes(canonicalMessage),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1));

        var verified = WeChatPaySignatureVerifier.Verify(
            publicKeyPem,
            timestamp,
            nonce,
            body,
            signature,
            path);

        Assert.IsTrue(verified);
    }
}
