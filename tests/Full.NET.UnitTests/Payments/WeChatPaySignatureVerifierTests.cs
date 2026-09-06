using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Payments.Domain;

namespace Full.NET.UnitTests.Payments;

[TestClass]
public sealed class WeChatPaySignatureVerifierTests
{
    [TestMethod]
    public void Verify_accepts_signature_signed_with_platform_private_key()
    {
        using var rsa = RSA.Create(2048);
        var publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();
        const string body = """{"id":"evt-test"}""";
        const string path = "/api/v1/payments/wechat-native/notify/00000000-0000-0000-0000-000000000001";
        const string timestamp = "1556208000";
        const string nonce = "593BEC0C930BF1AFEB40B4A08C8FB242";
        var canonicalMessage = WeChatPaySigner.BuildCanonicalMessage(
            "POST",
            path,
            long.Parse(timestamp),
            nonce,
            body);
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
