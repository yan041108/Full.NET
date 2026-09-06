using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Providers;
using Full.NET.Modules.Notifications.Providers.AliyunSms;
using Full.NET.Modules.Notifications.Providers.Smtp;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class AliyunSmsNotificationProviderAdapterTests
{
    private const string ValidConfig =
        "{\"regionId\":\"cn-hangzhou\",\"signName\":\"FullNET\",\"accessKeyId\":\"LTAI-test\",\"verificationTemplateCode\":\"SMS_10001\"}";

    [TestMethod]
    public void Descriptor_exposes_closed_sms_aliyun_schema()
    {
        var adapter = new AliyunSmsNotificationProviderAdapter(
            new StubSecretResolver("secret"),
            new RecordingAliyunSmsTransport("unused"));

        Assert.AreEqual("sms.aliyun", adapter.Descriptor.ProviderTypeKey);
        Assert.AreEqual("1.0.0", adapter.Descriptor.AdapterVersion);
        CollectionAssert.AreEqual(new[] { "sms" }, adapter.Descriptor.SupportedChannelKeys.ToArray());
        CollectionAssert.AreEqual(
            new[] { "regionId", "signName", "accessKeyId", "verificationTemplateCode" },
            adapter.Descriptor.NonSecretFields.Select(field => field.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "accessKeySecret" }, adapter.Descriptor.SecretFieldKeys.ToArray());
        Assert.AreEqual("signed", adapter.Descriptor.ReceiptModeKey);
        Assert.AreEqual("sms", adapter.RecipientEndpointKindKey);
    }

    [TestMethod]
    public async Task Valid_config_sends_template_sms()
    {
        var transport = new RecordingAliyunSmsTransport("biz-123");
        var adapter = new AliyunSmsNotificationProviderAdapter(
            new StubSecretResolver("secret"),
            transport);

        var result = await adapter.SendAsync(
            new NotificationProviderRequest(
                Guid.CreateVersion7(),
                "sms",
                "13800138000",
                ValidConfig,
                "env://FULLNET_TEST_ALIYUN_SMS_SECRET",
                "SMS_NOTICE",
                """{"code":"123456"}""",
                "idem-1",
                []),
            CancellationToken.None);

        Assert.IsTrue(result.Accepted);
        Assert.AreEqual(NotificationDeliveryRetry.Succeeded, result.ResultCategory);
        Assert.AreEqual("biz-123", result.ProviderMessageId);
        Assert.AreEqual("SMS_NOTICE", transport.LastCommand?.TemplateCode);
        Assert.AreEqual("13800138000", transport.LastCommand?.PhoneNumber);
    }

    [TestMethod]
    public async Task Invalid_phone_or_template_param_fails_permanently()
    {
        var adapter = new AliyunSmsNotificationProviderAdapter(
            new StubSecretResolver("secret"),
            new RecordingAliyunSmsTransport("unused"));

        var badPhone = await adapter.SendAsync(
            new NotificationProviderRequest(
                Guid.CreateVersion7(),
                "sms",
                "23800138000",
                ValidConfig,
                "env://FULLNET_TEST_ALIYUN_SMS_SECRET",
                "SMS_NOTICE",
                """{"code":"123456"}""",
                "idem-2",
                []),
            CancellationToken.None);
        Assert.IsFalse(badPhone.Accepted);
        Assert.AreEqual(NotificationDeliveryRetry.Permanent, badPhone.ResultCategory);

        var badBody = await adapter.SendAsync(
            new NotificationProviderRequest(
                Guid.CreateVersion7(),
                "sms",
                "13800138000",
                ValidConfig,
                "env://FULLNET_TEST_ALIYUN_SMS_SECRET",
                "SMS_NOTICE",
                "not-json",
                "idem-3",
                []),
            CancellationToken.None);
        Assert.IsFalse(badBody.Accepted);
        Assert.AreEqual(NotificationDeliveryRetry.Permanent, badBody.ResultCategory);
    }

    [TestMethod]
    public void IsValidChinaMobilePhone_rejects_invalid_numbers()
    {
        Assert.IsTrue(AliyunSmsNotificationProviderAdapter.IsValidChinaMobilePhone("13800138000"));
        Assert.IsFalse(AliyunSmsNotificationProviderAdapter.IsValidChinaMobilePhone("23800138000"));
        Assert.IsFalse(AliyunSmsNotificationProviderAdapter.IsValidChinaMobilePhone("+8613800138000"));
    }

    private sealed class StubSecretResolver(string secret) : INotificationSecretResolver
    {
        public ValueTask<string?> ResolveAsync(string? secretReference, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(secret);
    }

    private sealed class RecordingAliyunSmsTransport(string bizId) : IAliyunSmsTransport
    {
        public AliyunSmsSendCommand? LastCommand { get; private set; }

        public ValueTask<string> SendAsync(
            AliyunSmsSendCommand command,
            CancellationToken cancellationToken)
        {
            LastCommand = command;
            return ValueTask.FromResult(bizId);
        }
    }
}
