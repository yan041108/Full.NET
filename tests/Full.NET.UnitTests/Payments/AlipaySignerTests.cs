using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Payments.Domain;

namespace Full.NET.UnitTests.Payments;

[TestClass]
public sealed class AlipaySignerTests
{
    [TestMethod]
    public void Sign_produces_verifiable_rsa_sha256_signature()
    {
        using var rsa = RSA.Create(2048);
        var privateKeyPem = rsa.ExportPkcs8PrivateKeyPem();
        const string content = "app_id=2021000123456789&charset=utf-8&method=alipay.trade.page.pay";

        var signature = AlipaySigner.Sign(content, privateKeyPem);
        var verified = rsa.VerifyData(
            Encoding.UTF8.GetBytes(content),
            Convert.FromBase64String(signature),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        Assert.IsTrue(verified);
    }

    [TestMethod]
    public void BuildSignContent_sorts_parameters_and_excludes_empty_values()
    {
        var content = AlipaySigner.BuildSignContent(new Dictionary<string, string>
        {
            ["app_id"] = "2021000123456789",
            ["charset"] = "utf-8",
            ["sign"] = "ignored",
            ["method"] = "alipay.trade.page.pay",
            ["empty"] = "",
        });

        Assert.AreEqual(
            "app_id=2021000123456789&charset=utf-8&method=alipay.trade.page.pay",
            content);
    }
}
