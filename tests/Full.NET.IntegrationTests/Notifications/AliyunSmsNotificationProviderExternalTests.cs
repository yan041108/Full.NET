using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Providers;
using Full.NET.Modules.Notifications.Providers.AliyunSms;
using Full.NET.Modules.Notifications.Providers.Smtp;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>
/// 真实阿里云短信外部门禁；凭据未配置时标记 Inconclusive，禁止把 Secret 写入仓库或测试输出。
/// </summary>
[TestClass]
public sealed class AliyunSmsNotificationProviderExternalTests
{
    private const string RegionVariable = "FULLNET_TEST_ALIYUN_SMS_REGION";
    private const string SignNameVariable = "FULLNET_TEST_ALIYUN_SMS_SIGN_NAME";
    private const string AccessKeyIdVariable = "FULLNET_TEST_ALIYUN_SMS_ACCESS_KEY_ID";
    private const string AccessKeySecretVariable = "FULLNET_TEST_ALIYUN_SMS_ACCESS_KEY_SECRET";
    private const string TemplateCodeVariable = "FULLNET_TEST_ALIYUN_SMS_TEMPLATE_CODE";
    private const string PhoneVariable = "FULLNET_TEST_ALIYUN_SMS_PHONE";

    [TestMethod]
    [TestCategory("ExternalAliyunSms")]
    public async Task Configured_aliyun_sms_accepts_one_template_message()
    {
        var regionId = Environment.GetEnvironmentVariable(RegionVariable);
        var signName = Environment.GetEnvironmentVariable(SignNameVariable);
        var accessKeyId = Environment.GetEnvironmentVariable(AccessKeyIdVariable);
        var accessKeySecret = Environment.GetEnvironmentVariable(AccessKeySecretVariable);
        var templateCode = Environment.GetEnvironmentVariable(TemplateCodeVariable);
        var phone = Environment.GetEnvironmentVariable(PhoneVariable);
        if (string.IsNullOrWhiteSpace(regionId)
            || string.IsNullOrWhiteSpace(signName)
            || string.IsNullOrWhiteSpace(accessKeyId)
            || string.IsNullOrWhiteSpace(accessKeySecret)
            || string.IsNullOrWhiteSpace(templateCode)
            || string.IsNullOrWhiteSpace(phone))
        {
            Assert.Inconclusive("External Aliyun SMS runtime variables are not configured.");
            return;
        }

        var services = new ServiceCollection();
        services.AddHttpClient(HttpAliyunSmsTransport.HttpClientName);
        await using var provider = services.BuildServiceProvider();
        var adapter = new AliyunSmsNotificationProviderAdapter(
            new EnvironmentNotificationSecretResolver(),
            new HttpAliyunSmsTransport(provider.GetRequiredService<IHttpClientFactory>()));

        var uniqueId = Guid.NewGuid().ToString("N");
        var config = $$"""
            {"regionId":"{{regionId}}","signName":"{{signName}}","accessKeyId":"{{accessKeyId}}"}
            """;
        var result = await adapter.SendAsync(
            new NotificationProviderRequest(
                Guid.CreateVersion7(),
                "sms",
                phone,
                config,
                $"env://{AccessKeySecretVariable}",
                templateCode,
                """{"code":"123456"}""",
                $"aliyun-sms-external-test:{uniqueId}",
                []),
            TestContext.CancellationToken);

        Assert.IsTrue(
            result.Accepted,
            $"Aliyun SMS rejected the message with stable category '{result.ResultCategory}'.");
        Assert.AreEqual(NotificationDeliveryRetry.Succeeded, result.ResultCategory);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.ProviderMessageId));
    }

    public TestContext TestContext { get; set; } = null!;
}
