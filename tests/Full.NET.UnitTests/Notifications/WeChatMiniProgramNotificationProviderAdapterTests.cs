using Full.NET.Abstractions.Time;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Providers;
using Full.NET.Modules.Notifications.Providers.Smtp;
using Full.NET.Modules.Notifications.Providers.WeChatMiniProgram;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class WeChatMiniProgramNotificationProviderAdapterTests
{
    private const string ValidConfig = """{"appId":"wx1234567890abcdef","defaultTemplateId":"TMPL-001"}""";

    [TestMethod]
    public void Descriptor_exposes_closed_im_wechat_miniprogram_schema()
    {
        var adapter = CreateAdapter(new RecordingTransport("msg-1"));

        Assert.AreEqual("im.wechat_miniprogram", adapter.Descriptor.ProviderTypeKey);
        Assert.AreEqual("1.0.0", adapter.Descriptor.AdapterVersion);
        CollectionAssert.AreEqual(new[] { "wechat_miniprogram" }, adapter.Descriptor.SupportedChannelKeys.ToArray());
        CollectionAssert.AreEqual(
            new[] { "appId", "defaultTemplateId" },
            adapter.Descriptor.NonSecretFields.Select(field => field.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "appSecret" }, adapter.Descriptor.SecretFieldKeys.ToArray());
        Assert.AreEqual("none", adapter.Descriptor.ReceiptModeKey);
        Assert.AreEqual("wechat_miniprogram", adapter.RecipientEndpointKindKey);
    }

    [TestMethod]
    public async Task Valid_config_sends_subscribe_message()
    {
        var transport = new RecordingTransport("msg-123");
        var adapter = CreateAdapter(transport);

        var result = await adapter.SendAsync(
            new NotificationProviderRequest(
                Guid.CreateVersion7(),
                "wechat_miniprogram",
                "oAbcdefghijklmnopqrstuv",
                ValidConfig,
                "env://FULLNET_TEST_WECHAT_SECRET",
                "TMPL-001",
                """{"thing1":{"value":"审批提醒"}}""",
                "idem-1",
                []),
            CancellationToken.None);

        Assert.IsTrue(result.Accepted);
        Assert.AreEqual(NotificationDeliveryRetry.Succeeded, result.ResultCategory);
        Assert.AreEqual("msg-123", result.ProviderMessageId);
        Assert.AreEqual("oAbcdefghijklmnopqrstuv", transport.LastCommand?.ToUserOpenId);
        Assert.AreEqual("TMPL-001", transport.LastCommand?.TemplateId);
    }

    [TestMethod]
    public async Task Invalid_openid_or_body_fails_permanently()
    {
        var adapter = CreateAdapter(new RecordingTransport("unused"));

        var badOpenId = await adapter.SendAsync(
            new NotificationProviderRequest(
                Guid.CreateVersion7(),
                "wechat_miniprogram",
                "bad openid",
                ValidConfig,
                "env://FULLNET_TEST_WECHAT_SECRET",
                "TMPL-001",
                """{"thing1":{"value":"x"}}""",
                "idem-2",
                []),
            CancellationToken.None);
        Assert.IsFalse(badOpenId.Accepted);

        var badBody = await adapter.SendAsync(
            new NotificationProviderRequest(
                Guid.CreateVersion7(),
                "wechat_miniprogram",
                "oAbcdefghijklmnopqrstuv",
                ValidConfig,
                "env://FULLNET_TEST_WECHAT_SECRET",
                "TMPL-001",
                "not-json",
                "idem-3",
                []),
            CancellationToken.None);
        Assert.IsFalse(badBody.Accepted);
    }

    [TestMethod]
    public void IsValidOpenId_rejects_invalid_values()
    {
        Assert.IsTrue(WeChatMiniProgramNotificationProviderAdapter.IsValidOpenId("oAbcdefghijklmnopqrstuv"));
        Assert.IsFalse(WeChatMiniProgramNotificationProviderAdapter.IsValidOpenId("bad openid"));
    }

    private static WeChatMiniProgramNotificationProviderAdapter CreateAdapter(IWeChatMiniProgramTransport transport)
    {
        var tokenCache = new WeChatMiniProgramAccessTokenCache(
            transport,
            new FixedClock(DateTimeOffset.Parse("2026-09-06T12:00:00Z")));
        return new WeChatMiniProgramNotificationProviderAdapter(
            new StubSecretResolver("secret"),
            tokenCache,
            transport);
    }

    private sealed class StubSecretResolver(string secret) : INotificationSecretResolver
    {
        public ValueTask<string?> ResolveAsync(string? secretReference, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(secret);
    }

    private sealed class RecordingTransport(string msgId) : IWeChatMiniProgramTransport
    {
        public WeChatMiniProgramSubscribeSendCommand? LastCommand { get; private set; }

        public ValueTask<WeChatMiniProgramAccessToken> GetAccessTokenAsync(
            string appId,
            string appSecret,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new WeChatMiniProgramAccessToken(
                "token-abc",
                DateTimeOffset.UtcNow.AddHours(2)));

        public ValueTask<WeChatMiniProgramSession> ExchangeJsCodeAsync(
            string appId,
            string appSecret,
            string jsCode,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new WeChatMiniProgramSession("oAbcdefghijklmnopqrstuv", null));

        public ValueTask<string> SendSubscribeMessageAsync(
            string accessToken,
            WeChatMiniProgramSubscribeSendCommand command,
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
