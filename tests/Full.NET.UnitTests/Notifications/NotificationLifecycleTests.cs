using System.Net;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Providers.AliyunSms;
using Full.NET.Modules.Notifications.Providers.DingTalk;
using Full.NET.Modules.Notifications.Providers.WeChatMiniProgram;
using Full.NET.Modules.Notifications.Providers.WeCom;

namespace Full.NET.UnitTests.Notifications;

/// <summary>冻结业务元数据，以及无法证明未发送的 HTTP 结果不能触发安全重试。</summary>
[TestClass]
public sealed class NotificationLifecycleTests
{
    [TestMethod]
    public void Metadata_freezes_expiry_resource_and_preserves_route_candidates()
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(5);
        var link = new NotificationDeepLink("workflow_todo", Guid.CreateVersion7());
        var json = NotificationMessageMetadata.Freeze("""[{"profileKey":"primary"}]""", expiry, link, 7);
        var actual = NotificationMessageMetadata.Read(json);
        Assert.AreEqual(expiry, actual.ExpiresAtUtc);
        Assert.AreEqual(link, actual.DeepLink);
        StringAssert.Contains(json, "primary");
        StringAssert.Contains(json, "\"policyVersion\":7");
        Assert.IsFalse(NotificationMessageMetadata.IsValid(new NotificationDeepLink("https://evil.test", link.ResourceId)));
        Assert.IsFalse(NotificationMessageMetadata.IsValid(link with { ResourceId = Guid.Empty }));
    }

    [TestMethod]
    [DataRow("network")]
    [DataRow("server")]
    [DataRow("malformed")]
    public async Task Ambiguous_send_response_is_unknown_for_all_http_providers(string failure)
    {
        var factory = new FailureFactory(failure);
        var sms = await Assert.ThrowsExactlyAsync<AliyunSmsTransportException>(async () =>
            await new HttpAliyunSmsTransport(factory).SendAsync(new AliyunSmsSendCommand("cn-hangzhou", "key", "secret",
                "13800138000", "sign", "SMS_TEST", "{}", "same-key"), default));
        Assert.AreEqual(AliyunSmsTransportFailureKind.Unknown, sms.FailureKind);
        var mini = await Assert.ThrowsExactlyAsync<WeChatMiniProgramTransportException>(async () =>
            await new HttpWeChatMiniProgramTransport(factory).SendSubscribeMessageAsync("token",
                new WeChatMiniProgramSubscribeSendCommand("openid", "template", null, "{}", "same-key"), default));
        Assert.AreEqual(WeChatMiniProgramTransportFailureKind.Unknown, mini.FailureKind);
        var wecom = await Assert.ThrowsExactlyAsync<WeComTransportException>(async () =>
            await new HttpWeComTransport(factory).SendTextAsync("token", new WeComSendTextCommand("user", 1, "body", "same-key"), default));
        Assert.AreEqual(WeComTransportFailureKind.Unknown, wecom.FailureKind);
        var ding = await Assert.ThrowsExactlyAsync<DingTalkTransportException>(async () =>
            await new HttpDingTalkTransport(factory).CreateAndDeliverAsync("token",
                new DingTalkCreateAndDeliverCommand("template", "same-key", "space", "robot", null, new Dictionary<string, string>()), default));
        Assert.AreEqual(DingTalkTransportFailureKind.Unknown, ding.FailureKind);
    }

    private sealed class FailureFactory(string failure) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FailureHandler(failure));
    }

    private sealed class FailureHandler(string failure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            failure == "network" ? throw new HttpRequestException("模拟响应丢失")
                : Task.FromResult(new HttpResponseMessage(failure == "server" ? HttpStatusCode.BadGateway : HttpStatusCode.OK)
                { Content = new StringContent("invalid-json") });
    }
}
