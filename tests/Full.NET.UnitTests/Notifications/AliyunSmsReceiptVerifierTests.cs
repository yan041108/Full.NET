using System.Text;
using System.Text.Json;
using Full.NET.Modules.Notifications.Providers.AliyunSms;
using Microsoft.Extensions.Configuration;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class AliyunSmsReceiptVerifierTests
{
    [TestMethod]
    public void Valid_signature_maps_delivery_status()
    {
        Environment.SetEnvironmentVariable("FULLNET_TEST_ALIYUN_SMS_RECEIPT_SECRET", "receipt-secret");
        var body = Encoding.UTF8.GetBytes(
            """
            [
              {
                "phone_number": "13800138000",
                "send_time": "2026-09-06 10:00:00",
                "report_time": "2026-09-06 10:00:01",
                "success": true,
                "err_code": "DELIVERED",
                "biz_id": "biz-001"
              }
            ]
            """);
        var verifier = CreateVerifier();
        var signature = AliyunSmsReceiptVerifier.Sign(body, "receipt-secret");
        var result = verifier.Verify(
            body,
            new Dictionary<string, string>
            {
                [AliyunSmsReceiptVerifier.SignatureHeaderName] = signature,
            });

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("delivered", result.Value!.MappedStatusKey);
        Assert.AreEqual("biz-001", result.Value.ProviderMessageId);
        Environment.SetEnvironmentVariable("FULLNET_TEST_ALIYUN_SMS_RECEIPT_SECRET", null);
    }

    [TestMethod]
    public void Invalid_signature_fails_closed()
    {
        Environment.SetEnvironmentVariable("FULLNET_TEST_ALIYUN_SMS_RECEIPT_SECRET", "receipt-secret");
        var body = """{"receiptIdempotencyKey":"x"}"""u8.ToArray();
        var verifier = CreateVerifier();
        var result = verifier.Verify(
            body,
            new Dictionary<string, string>
            {
                [AliyunSmsReceiptVerifier.SignatureHeaderName] = "00",
            });
        Assert.IsFalse(result.IsSuccess);
        Environment.SetEnvironmentVariable("FULLNET_TEST_ALIYUN_SMS_RECEIPT_SECRET", null);
    }

    private static AliyunSmsReceiptVerifier CreateVerifier()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:Providers:AliyunSms:ReceiptSecretReference"] =
                    "env://FULLNET_TEST_ALIYUN_SMS_RECEIPT_SECRET",
            })
            .Build();
        return new AliyunSmsReceiptVerifier(configuration);
    }
}
