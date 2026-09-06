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
        Environment.SetEnvironmentVariable("FULLNET_TEST_DINGTALK_RECEIPT_SECRET", "receipt-secret");
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
        var verifier = CreateVerifier();
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
        Environment.SetEnvironmentVariable("FULLNET_TEST_DINGTALK_RECEIPT_SECRET", null);
    }

    [TestMethod]
    public void Invalid_signature_fails_closed()
    {
        Environment.SetEnvironmentVariable("FULLNET_TEST_DINGTALK_RECEIPT_SECRET", "receipt-secret");
        var body = """{"outTrackId":"x"}"""u8.ToArray();
        var verifier = CreateVerifier();
        var result = verifier.Verify(
            body,
            new Dictionary<string, string>
            {
                [DingTalkReceiptVerifier.SignatureHeaderName] = "00",
            });
        Assert.IsFalse(result.IsSuccess);
        Environment.SetEnvironmentVariable("FULLNET_TEST_DINGTALK_RECEIPT_SECRET", null);
    }

    private static DingTalkReceiptVerifier CreateVerifier()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:Providers:DingTalk:ReceiptSecretReference"] =
                    "env://FULLNET_TEST_DINGTALK_RECEIPT_SECRET",
            })
            .Build();
        return new DingTalkReceiptVerifier(configuration);
    }
}
