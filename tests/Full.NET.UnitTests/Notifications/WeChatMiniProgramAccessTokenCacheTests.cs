using Full.NET.Abstractions.Time;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Providers.WeChatMiniProgram;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class WeChatMiniProgramAccessTokenCacheTests
{
    [TestMethod]
    public async Task Cache_reuses_token_until_expiry_skew()
    {
        var clock = new MutableClock(DateTimeOffset.Parse("2026-09-06T12:00:00Z"));
        var transport = new CountingTransport(() => clock.UtcNow.AddMinutes(30));
        var cache = new WeChatMiniProgramAccessTokenCache(transport, clock);

        var first = await cache.GetOrRefreshAsync("app-id", "secret", CancellationToken.None);
        clock.UtcNow = clock.UtcNow.AddMinutes(10);
        var second = await cache.GetOrRefreshAsync("app-id", "secret", CancellationToken.None);

        Assert.AreEqual("token-1", first);
        Assert.AreEqual("token-1", second);
        Assert.AreEqual(1, transport.TokenRequests);
    }

    [TestMethod]
    public void OpenId_fingerprint_is_stable_and_lowercase()
    {
        var first = WeChatMiniProgramOpenIdFingerprint.Compute("oAbcdefghijklmnopqrstuv");
        var second = WeChatMiniProgramOpenIdFingerprint.Compute("oAbcdefghijklmnopqrstuv");
        Assert.AreEqual(first, second);
        Assert.AreEqual(64, first.Length);
        Assert.IsFalse(first.Any(char.IsUpper));
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class CountingTransport(Func<DateTimeOffset> expiresAtUtc) : IWeChatMiniProgramTransport
    {
        public int TokenRequests { get; private set; }

        public ValueTask<WeChatMiniProgramAccessToken> GetAccessTokenAsync(
            string appId,
            string appSecret,
            CancellationToken cancellationToken)
        {
            TokenRequests++;
            return ValueTask.FromResult(new WeChatMiniProgramAccessToken(
                $"token-{TokenRequests}",
                expiresAtUtc()));
        }

        public ValueTask<WeChatMiniProgramSession> ExchangeJsCodeAsync(
            string appId,
            string appSecret,
            string jsCode,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new WeChatMiniProgramSession("oAbcdefghijklmnopqrstuv", null));

        public ValueTask<string> SendSubscribeMessageAsync(
            string accessToken,
            WeChatMiniProgramSubscribeSendCommand command,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult("msg-1");
    }
}
