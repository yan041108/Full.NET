using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Payments.Domain;

namespace Full.NET.UnitTests.Payments;

[TestClass]
public sealed class WeChatPaySignerTests
{
    [TestMethod]
    public void BuildCanonicalMessage_uses_wechat_v3_line_format()
    {
        const string body = """{"appid":"wx123"}""";
        var message = WeChatPaySigner.BuildCanonicalMessage(
            "POST",
            "/v3/pay/transactions/native",
            1_556_208_000,
            "593BEC0C930BF1AFEB40B4A08C8FB242",
            body);

        Assert.AreEqual(
            """
            POST
            /v3/pay/transactions/native
            1556208000
            593BEC0C930BF1AFEB40B4A08C8FB242
            {"appid":"wx123"}

            """.Replace("\r\n", "\n"),
            message);
    }

    [TestMethod]
    public void Sign_produces_verifiable_rsa_sha256_signature()
    {
        using var rsa = RSA.Create(2048);
        var privateKeyPem = rsa.ExportPkcs8PrivateKeyPem();
        var canonicalMessage = WeChatPaySigner.BuildCanonicalMessage(
            "POST",
            "/v3/pay/transactions/native",
            1_556_208_000,
            "593BEC0C930BF1AFEB40B4A08C8FB242",
            """{"appid":"wx123"}""");

        var signature = WeChatPaySigner.Sign(canonicalMessage, privateKeyPem);
        var signatureBytes = Convert.FromBase64String(signature);
        var verified = rsa.VerifyData(
            Encoding.UTF8.GetBytes(canonicalMessage),
            signatureBytes,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        Assert.IsTrue(verified);
    }

    [TestMethod]
    public void BuildAuthorizationHeader_contains_required_wechat_fields()
    {
        var header = WeChatPaySigner.BuildAuthorizationHeader(
            "1900000109",
            "7132D72A03E93CDDF8C03BBD1F3700AD9091D",
            1_556_208_000,
            "593BEC0C930BF1AFEB40B4A08C8FB242",
            "test-signature");

        StringAssert.StartsWith(header, "WECHATPAY2-SHA256-RSA2048 ");
        StringAssert.Contains(header, "mchid=\"1900000109\"");
        StringAssert.Contains(header, "serial_no=\"7132D72A03E93CDDF8C03BBD1F3700AD9091D\"");
        StringAssert.Contains(header, "signature=\"test-signature\"");
    }
}
