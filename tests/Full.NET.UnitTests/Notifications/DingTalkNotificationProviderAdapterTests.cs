using Full.NET.Abstractions.Time;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Providers;
using Full.NET.Modules.Notifications.Providers.DingTalk;
using Full.NET.Modules.Notifications.Providers.Smtp;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class DingTalkNotificationProviderAdapterTests
{
    private const string ValidConfig =
        """{"appKey":"ding-app","agentId":"123456","cardTemplateId":"tpl-001","robotCode":"robot-01","callbackRouteKey":"fullnet"}""";

    [TestMethod]
    public void Descriptor_exposes_closed_im_dingtalk_schema()
    {
        var adapter = CreateAdapter(new RecordingDingTalkTransport("track-1"));

        Assert.AreEqual("im.dingtalk", adapter.Descriptor.ProviderTypeKey);
        Assert.AreEqual("1.0.0", adapter.Descriptor.AdapterVersion);
        CollectionAssert.AreEqual(new[] { "dingtalk" }, adapter.Descriptor.SupportedChannelKeys.ToArray());
        CollectionAssert.AreEqual(
            new[] { "appKey", "agentId", "cardTemplateId", "robotCode", "callbackRouteKey" },
            adapter.Descriptor.NonSecretFields.Select(field => field.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "appSecret" }, adapter.Descriptor.SecretFieldKeys.ToArray());
        Assert.AreEqual("signed", adapter.Descriptor.ReceiptModeKey);
        Assert.AreEqual("dingtalk", adapter.RecipientEndpointKindKey);
    }

    [TestMethod]
    public async Task Valid_config_sends_interactive_card()
    {
        var transport = new RecordingDingTalkTransport("track-123");
        var adapter = CreateAdapter(transport);

        var result = await adapter.SendAsync(
            new NotificationProviderRequest(
                Guid.CreateVersion7(),
                "dingtalk",
                "manager001",
                ValidConfig,
                "env://FULLNET_TEST_DINGTALK_SECRET",
                "审批提醒",
                """{"title":"审批提醒","content":"您有一条待办"}""",
                "idem-1",
                []),
            CancellationToken.None);

        Assert.IsTrue(result.Accepted);
        Assert.AreEqual(NotificationDeliveryRetry.Succeeded, result.ResultCategory);
        Assert.AreEqual("track-123", result.ProviderMessageId);
        Assert.AreEqual("dtv1.card//IM_ROBOT.manager001", transport.LastCommand?.OpenSpaceId);
        Assert.AreEqual("tpl-001", transport.LastCommand?.CardTemplateId);
        Assert.AreEqual("审批提醒", transport.LastCommand?.CardParamMap["title"]);
    }

    [TestMethod]
    public async Task Invalid_user_or_body_fails_permanently()
    {
        var adapter = CreateAdapter(new RecordingDingTalkTransport("unused"));

        var badUser = await adapter.SendAsync(
            new NotificationProviderRequest(
                Guid.CreateVersion7(),
                "dingtalk",
                "bad user",
                ValidConfig,
                "env://FULLNET_TEST_DINGTALK_SECRET",
                "标题",
                "正文",
                "idem-2",
                []),
            CancellationToken.None);
        Assert.IsFalse(badUser.Accepted);
        Assert.AreEqual(NotificationDeliveryRetry.Permanent, badUser.ResultCategory);

        var badBody = await adapter.SendAsync(
            new NotificationProviderRequest(
                Guid.CreateVersion7(),
                "dingtalk",
                "manager001",
                ValidConfig,
                "env://FULLNET_TEST_DINGTALK_SECRET",
                "",
                "",
                "idem-3",
                []),
            CancellationToken.None);
        Assert.IsFalse(badBody.Accepted);
        Assert.AreEqual(NotificationDeliveryRetry.Permanent, badBody.ResultCategory);
    }

    [TestMethod]
    public void IsValidDingTalkUserId_rejects_invalid_ids()
    {
        Assert.IsTrue(DingTalkNotificationProviderAdapter.IsValidDingTalkUserId("manager001"));
        Assert.IsFalse(DingTalkNotificationProviderAdapter.IsValidDingTalkUserId("bad user"));
        Assert.IsFalse(DingTalkNotificationProviderAdapter.IsValidDingTalkUserId(""));
    }

    private static DingTalkNotificationProviderAdapter CreateAdapter(IDingTalkTransport transport)
    {
        var tokenCache = new DingTalkAccessTokenCache(
            transport,
            new FixedClock(DateTimeOffset.Parse("2026-09-06T12:00:00Z")));
        return new DingTalkNotificationProviderAdapter(
            new StubSecretResolver("secret"),
            tokenCache,
            transport);
    }

    private sealed class StubSecretResolver(string secret) : INotificationSecretResolver
    {
        public ValueTask<string?> ResolveAsync(string? secretReference, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(secret);
    }

    private sealed class RecordingDingTalkTransport(string outTrackId) : IDingTalkTransport
    {
        public DingTalkCreateAndDeliverCommand? LastCommand { get; private set; }

        public ValueTask<DingTalkAccessToken> GetAccessTokenAsync(
            string appKey,
            string appSecret,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new DingTalkAccessToken(
                "token-abc",
                DateTimeOffset.UtcNow.AddHours(2)));

        public ValueTask<string> CreateAndDeliverAsync(
            string accessToken,
            DingTalkCreateAndDeliverCommand command,
            CancellationToken cancellationToken)
        {
            LastCommand = command;
            return ValueTask.FromResult(outTrackId);
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}
