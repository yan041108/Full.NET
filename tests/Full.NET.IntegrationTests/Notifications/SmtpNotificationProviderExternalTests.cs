using System.Globalization;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Providers;
using Full.NET.Modules.Notifications.Providers.Smtp;

namespace Full.NET.IntegrationTests.Notifications;

[TestClass]
public sealed class SmtpNotificationProviderExternalTests
{
    private const string HostVariable = "FULLNET_TEST_SMTP_HOST";
    private const string PortVariable = "FULLNET_TEST_SMTP_PORT";
    private const string UsernameVariable = "FULLNET_TEST_SMTP_USERNAME";
    private const string PasswordVariable = "FULLNET_TEST_SMTP_PASSWORD";
    private const string RecipientVariable = "FULLNET_TEST_SMTP_RECIPIENT";

    [TestMethod]
    [TestCategory("ExternalSmtp")]
    public async Task Configured_smtp_server_accepts_one_plain_text_self_test()
    {
        var host = Environment.GetEnvironmentVariable(HostVariable);
        var portText = Environment.GetEnvironmentVariable(PortVariable);
        var username = Environment.GetEnvironmentVariable(UsernameVariable);
        var recipient = Environment.GetEnvironmentVariable(RecipientVariable);
        var password = Environment.GetEnvironmentVariable(PasswordVariable);
        if (string.IsNullOrWhiteSpace(host)
            || !int.TryParse(portText, CultureInfo.InvariantCulture, out var port)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(recipient)
            || string.IsNullOrEmpty(password))
        {
            Assert.Inconclusive("External SMTP runtime variables are not configured.");
            return;
        }

        var uniqueId = Guid.NewGuid().ToString("N");
        var config = $$"""
            {"fromAddress":"{{username}}","fromDisplayName":"Full.NET SMTP Test","host":"{{host}}","port":{{port}},"secureSocketMode":"ssl_on_connect","username":"{{username}}"}
            """;
        var adapter = new SmtpNotificationProviderAdapter(
            new EnvironmentNotificationSecretResolver(),
            new MailKitSmtpTransport());
        var result = await adapter.SendAsync(
            new NotificationProviderRequest(
                Guid.NewGuid(),
                "email",
                recipient,
                config,
                $"env://{PasswordVariable}",
                $"Full.NET SMTP external test {uniqueId}",
                $"This is a Full.NET SMTP provider connectivity test. Correlation: {uniqueId}",
                $"smtp-external-test:{uniqueId}"),
            TestContext.CancellationToken);

        Assert.IsTrue(
            result.Accepted,
            $"SMTP server rejected the message with stable category '{result.ResultCategory}'.");
        Assert.AreEqual(NotificationDeliveryRetry.Succeeded, result.ResultCategory);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.ProviderMessageId));
    }

    public TestContext TestContext { get; set; } = null!;
}
