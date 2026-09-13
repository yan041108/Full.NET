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

    [TestMethod]
    public async Task Cache_evicts_oldest_entry_at_capacity()
    {
        var clock = new MutableClock(DateTimeOffset.Parse("2026-09-06T12:00:00Z"));
        var transport = new CountingTransport(() => clock.UtcNow.AddMinutes(30));
        var cache = new WeComAccessTokenCache(transport, clock);
        await cache.GetOrRefreshAsync("old", "secret", CancellationToken.None);
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        for (var i = 0; i < 1024; i++)
            await cache.GetOrRefreshAsync($"corp-id-{i}", "secret", CancellationToken.None);
        var requests = transport.TokenRequests;
        await cache.GetOrRefreshAsync("old", "secret", CancellationToken.None);
        Assert.AreEqual(requests + 1, transport.TokenRequests);
    }

    [TestMethod]
    public async Task Cache_removes_expired_entries_for_inactive_keys()
    {
        var clock = new MutableClock(DateTimeOffset.Parse("2026-09-06T12:00:00Z"));
        var transport = new CountingTransport(() => clock.UtcNow.AddMinutes(30));
        var cache = new WeComAccessTokenCache(transport, clock);
        await cache.GetOrRefreshAsync("old", "secret", CancellationToken.None);
        clock.UtcNow = clock.UtcNow.AddHours(1);
        await cache.GetOrRefreshAsync("new", "secret", CancellationToken.None);
        // 检查持有的条目数，过期后拒绝命中本身不能证明旧对象已经回收。
        var entries = (System.Collections.IDictionary)typeof(WeComAccessTokenCache)
            .GetField("_tokens", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(cache)!;
        Assert.AreEqual(1, entries.Count);
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
