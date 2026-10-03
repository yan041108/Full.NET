using System.Text;
using Full.NET.Modules.Notifications.Providers.DingTalk;
using Microsoft.Extensions.Configuration;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class DingTalkReceiptVerifierTests
{
    [TestMethod]
    public void Valid_signature_maps_delivery_status()
    {
        // MethodLevel 并行运行时使用独立密钥引用，避免另一用例清理进程环境变量导致误失败。
        var secretVariable = "FULLNET_TEST_DINGTALK_RECEIPT_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(secretVariable, "receipt-secret");
        try
        {
            var body = Encoding.UTF8.GetBytes(
                """
                {
                  "outTrackId": "track-001",
                  "userId": "manager001",
                  "eventTime": "2026-09-06T10:00:01Z",
                  "status": "delivered",
                  "errorCode": "OK"
                }
                """);
            var verifier = CreateVerifier(secretVariable);
            var signature = DingTalkReceiptVerifier.Sign(body, "receipt-secret");
            var result = verifier.Verify(
                body,
                new Dictionary<string, string>
                {
                    [DingTalkReceiptVerifier.SignatureHeaderName] = signature,
                });

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("delivered", result.Value!.MappedStatusKey);
            Assert.AreEqual("track-001", result.Value.ProviderMessageId);
        }
        finally
        {
            Environment.SetEnvironmentVariable(secretVariable, null);
        }
    }

    [TestMethod]
    public void Invalid_signature_fails_closed()
    {
        var secretVariable = "FULLNET_TEST_DINGTALK_RECEIPT_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(secretVariable, "receipt-secret");
        try
        {
            var body = """{"outTrackId":"x"}"""u8.ToArray();
            var verifier = CreateVerifier(secretVariable);
            var result = verifier.Verify(
                body,
                new Dictionary<string, string>
                {
                    [DingTalkReceiptVerifier.SignatureHeaderName] = "00",
                });
            Assert.IsFalse(result.IsSuccess);
        }
        finally
        {
            Environment.SetEnvironmentVariable(secretVariable, null);
        }
    }

    private static DingTalkReceiptVerifier CreateVerifier(string secretVariable)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:Providers:DingTalk:ReceiptSecretReference"] =
                    "env://" + secretVariable,
            })
            .Build();
        return new DingTalkReceiptVerifier(configuration);
    }
}
