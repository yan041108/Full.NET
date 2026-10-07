using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Providers;
using Full.NET.Modules.Notifications.Providers.Smtp;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>使用局部受信根和真实 SMTP 会话验证接受、确认丢失以及明确拒收，不改变系统信任。</summary>
[TestClass]
public sealed class SmtpProtocolOutcomeTests
{
    /// <summary>两种 TLS 模式覆盖 DATA 接受、确认丢失、接受后断线、临时拒收与认证拒绝。</summary>
    [TestMethod]
    [DataRow(false, "accepted")]
    [DataRow(true, "accepted")]
    [DataRow(false, "ack_lost")]
    [DataRow(true, "ack_lost")]
    [DataRow(false, "quit_lost")]
    [DataRow(true, "quit_lost")]
    [DataRow(false, "temporary_rejection")]
    [DataRow(true, "temporary_rejection")]
    [DataRow(false, "authentication_rejection")]
    [DataRow(true, "authentication_rejection")]
    public async Task Real_protocol_outcome_is_classified_without_duplicate_retry(bool startTls, string outcome)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        await using var inbox = new ControlledSmtpInbox(startTls);
        var server = inbox.ReceiveAsync(outcome, timeout.Token);
        var adapter = new SmtpNotificationProviderAdapter(new ControlledSmtpInbox.ProtocolSecretResolver(), inbox.CreateTransport());
        var config = System.Text.Json.JsonSerializer.Serialize(inbox.Configuration);
        var request = new NotificationProviderRequest(Guid.NewGuid(), "email", "receiver@example.test", config,
            "test-secret", "Verification", "protocol-private-code", "stable-protocol-key", []);
        var result = await adapter.SendAsync(request, timeout.Token);
        var observation = await server;
        var accepted = outcome is "accepted" or "quit_lost";
        Assert.AreEqual(accepted, result.Accepted);
        Assert.AreEqual(outcome switch
        {
            "ack_lost" => NotificationDeliveryRetry.Unknown,
            "temporary_rejection" => NotificationDeliveryRetry.Transient,
            "authentication_rejection" => NotificationDeliveryRetry.Permanent,
            _ => NotificationDeliveryRetry.Succeeded,
        }, result.ResultCategory);
        Assert.IsTrue(observation.Authenticated || outcome == "authentication_rejection");
        if (outcome == "authentication_rejection") Assert.IsNull(observation.Message);
        else
        {
            Assert.IsNotNull(observation.Message);
            Assert.AreEqual("receiver@example.test", observation.Message.To.Mailboxes.Single().Address);
            Assert.AreEqual("Verification", observation.Message.Subject);
            var textBody = observation.Message.TextBody;
            var messageId = observation.Message.MessageId;
            Assert.IsNotNull(textBody);
            Assert.IsNotNull(messageId);
            Assert.AreEqual("protocol-private-code", textBody.Trim());
            Assert.IsTrue(messageId.StartsWith("fullnet-", StringComparison.Ordinal));
            if (accepted) Assert.AreEqual(messageId, result.ProviderMessageId);
        }
        Assert.IsFalse(result.ToString().Contains("protocol-private", StringComparison.Ordinal));
        Assert.IsFalse(result.ToString().Contains("protocol-password", StringComparison.Ordinal));
    }

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Inbox_cleanup_cancels_and_observes_an_unconnected_server()
    {
        var inbox = new ControlledSmtpInbox(false);
        var pending = inbox.ReceiveAsync("accepted", TestContext.CancellationToken);
        await inbox.DisposeAsync();
        Assert.IsTrue(pending.IsCanceled, "夹具退出必须取消并观察尚未接收连接的任务。");
    }
}
