using Full.NET.Abstractions.Time;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Providers;
using Full.NET.Modules.Notifications.Providers.Smtp;
using Full.NET.Modules.Notifications.Providers.WeCom;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class WeComNotificationProviderAdapterTests
{
    private const string ValidConfig = """{"corpId":"ww123456","agentId":"1000002"}""";

    [TestMethod]
    public void Descriptor_exposes_closed_im_wecom_schema()
    {
        var adapter = CreateAdapter(new RecordingWeComTransport("msg-1"));

        Assert.AreEqual("im.wecom", adapter.Descriptor.ProviderTypeKey);
        Assert.AreEqual("1.0.0", adapter.Descriptor.AdapterVersion);
        CollectionAssert.AreEqual(new[] { "wecom" }, adapter.Descriptor.SupportedChannelKeys.ToArray());
        CollectionAssert.AreEqual(
            new[] { "corpId", "agentId" },
            adapter.Descriptor.NonSecretFields.Select(field => field.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "corpSecret" }, adapter.Descriptor.SecretFieldKeys.ToArray());
        Assert.AreEqual("none", adapter.Descriptor.ReceiptModeKey);
        Assert.AreEqual("wecom", adapter.RecipientEndpointKindKey);
    }

    [TestMethod]
    public async Task Valid_config_sends_text_message()
    {
        var transport = new RecordingWeComTransport("msg-123");
        var adapter = CreateAdapter(transport);

        var result = await adapter.SendAsync(
            new NotificationProviderRequest(
                Guid.CreateVersion7(),
                "wecom",
                "zhangsan",
                ValidConfig,
                "env://FULLNET_TEST_WECOM_SECRET",
                "审批提醒",
                "您有一条待办",
                "idem-1",
                []),
            CancellationToken.None);

        Assert.IsTrue(result.Accepted);
        Assert.AreEqual(NotificationDeliveryRetry.Succeeded, result.ResultCategory);
        Assert.AreEqual("msg-123", result.ProviderMessageId);
        Assert.AreEqual("zhangsan", transport.LastCommand?.ToUserId);
        Assert.AreEqual("审批提醒\n您有一条待办", transport.LastCommand?.Content);
    }

    [TestMethod]
    public async Task Invalid_user_or_content_fails_permanently()
    {
        var adapter = CreateAdapter(new RecordingWeComTransport("unused"));

        var badUser = await adapter.SendAsync(
            new NotificationProviderRequest(
                Guid.CreateVersion7(),
                "wecom",
                "bad user",
                ValidConfig,
                "env://FULLNET_TEST_WECOM_SECRET",
                "标题",
                "正文",
                "idem-2",
                []),
            CancellationToken.None);
        Assert.IsFalse(badUser.Accepted);

        var badTitle = await adapter.SendAsync(
            new NotificationProviderRequest(
                Guid.CreateVersion7(),
                "wecom",
                "zhangsan",
                ValidConfig,
                "env://FULLNET_TEST_WECOM_SECRET",
                "",
                "正文",
                "idem-3",
                []),
            CancellationToken.None);
        Assert.IsFalse(badTitle.Accepted);
    }

    [TestMethod]
    public void IsValidWeComUserId_rejects_invalid_ids()
    {
        Assert.IsTrue(WeComNotificationProviderAdapter.IsValidWeComUserId("zhangsan"));
        Assert.IsFalse(WeComNotificationProviderAdapter.IsValidWeComUserId("bad user"));
    }

    private static WeComNotificationProviderAdapter CreateAdapter(IWeComTransport transport)
    {
        var tokenCache = new WeComAccessTokenCache(
            transport,
            new FixedClock(DateTimeOffset.Parse("2026-09-06T12:00:00Z")));
        return new WeComNotificationProviderAdapter(
            new StubSecretResolver("secret"),
            tokenCache,
            transport);
    }

    private sealed class StubSecretResolver(string secret) : INotificationSecretResolver
    {
        public ValueTask<string?> ResolveAsync(string? secretReference, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(secret);
    }

    private sealed class RecordingWeComTransport(string msgId) : IWeComTransport
    {
        public WeComSendTextCommand? LastCommand { get; private set; }

        public ValueTask<WeComAccessToken> GetAccessTokenAsync(
            string corpId,
            string corpSecret,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new WeComAccessToken(
                "token-abc",
                DateTimeOffset.UtcNow.AddHours(2)));

        public ValueTask<string> SendTextAsync(
            string accessToken,
            WeComSendTextCommand command,
            CancellationToken cancellationToken)
        {
            LastCommand = command;
            return ValueTask.FromResult(msgId);
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}
