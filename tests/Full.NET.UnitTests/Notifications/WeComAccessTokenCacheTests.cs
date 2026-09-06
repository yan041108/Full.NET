using Full.NET.Abstractions.Time;
using Full.NET.Modules.Notifications.Providers.WeCom;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class WeComAccessTokenCacheTests
{
    [TestMethod]
    public async Task Cache_reuses_token_until_expiry_skew()
    {
        var clock = new MutableClock(DateTimeOffset.Parse("2026-09-06T12:00:00Z"));
        var transport = new CountingTransport(() => clock.UtcNow.AddMinutes(30));
        var cache = new WeComAccessTokenCache(transport, clock);

        var first = await cache.GetOrRefreshAsync("corp-id", "secret", CancellationToken.None);
        clock.UtcNow = clock.UtcNow.AddMinutes(10);
        var second = await cache.GetOrRefreshAsync("corp-id", "secret", CancellationToken.None);

        Assert.AreEqual("token-1", first);
        Assert.AreEqual("token-1", second);
        Assert.AreEqual(1, transport.TokenRequests);
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class CountingTransport(Func<DateTimeOffset> expiresAtUtc) : IWeComTransport
    {
        public int TokenRequests { get; private set; }

        public ValueTask<WeComAccessToken> GetAccessTokenAsync(
            string corpId,
            string corpSecret,
            CancellationToken cancellationToken)
        {
            TokenRequests++;
            return ValueTask.FromResult(new WeComAccessToken(
                $"token-{TokenRequests}",
                expiresAtUtc()));
        }

        public ValueTask<string> SendTextAsync(
            string accessToken,
            WeComSendTextCommand command,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult("msg-1");
    }
}
