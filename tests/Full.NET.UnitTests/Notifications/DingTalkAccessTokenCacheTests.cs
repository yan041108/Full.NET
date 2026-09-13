using Full.NET.Abstractions.Time;
using Full.NET.Modules.Notifications.Providers.DingTalk;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class DingTalkAccessTokenCacheTests
{
    [TestMethod]
    public async Task Cache_reuses_token_until_expiry_skew()
    {
        var clock = new MutableClock(DateTimeOffset.Parse("2026-09-06T12:00:00Z"));
        var transport = new CountingTransport(() => clock.UtcNow.AddMinutes(30));
        var cache = new DingTalkAccessTokenCache(transport, clock);

        var first = await cache.GetOrRefreshAsync("app-key", "secret", CancellationToken.None);
        clock.UtcNow = clock.UtcNow.AddMinutes(10);
        var second = await cache.GetOrRefreshAsync("app-key", "secret", CancellationToken.None);

        Assert.AreEqual("token-1", first);
        Assert.AreEqual("token-1", second);
        Assert.AreEqual(1, transport.TokenRequests);
    }

    [TestMethod]
    public async Task Cache_refreshes_token_after_expiry_skew()
    {
        var clock = new MutableClock(DateTimeOffset.Parse("2026-09-06T12:00:00Z"));
        var transport = new CountingTransport(() => clock.UtcNow.AddMinutes(30));
        var cache = new DingTalkAccessTokenCache(transport, clock);

        var first = await cache.GetOrRefreshAsync("app-key", "secret", CancellationToken.None);
        clock.UtcNow = clock.UtcNow.AddMinutes(31);
        var second = await cache.GetOrRefreshAsync("app-key", "secret", CancellationToken.None);

        Assert.AreEqual("token-1", first);
        Assert.AreEqual("token-2", second);
        Assert.AreEqual(2, transport.TokenRequests);
    }

    [TestMethod]
    public async Task Cache_evicts_oldest_entry_at_capacity()
    {
        var clock = new MutableClock(DateTimeOffset.Parse("2026-09-06T12:00:00Z"));
        var transport = new CountingTransport(() => clock.UtcNow.AddMinutes(30));
        var cache = new DingTalkAccessTokenCache(transport, clock);
        await cache.GetOrRefreshAsync("old", "secret", CancellationToken.None);
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        for (var i = 0; i < 1024; i++)
            await cache.GetOrRefreshAsync($"app-key-{i}", "secret", CancellationToken.None);
        var requests = transport.TokenRequests;
        await cache.GetOrRefreshAsync("old", "secret", CancellationToken.None);
        Assert.AreEqual(requests + 1, transport.TokenRequests);
    }

    [TestMethod]
    public async Task Cache_removes_expired_entries_for_inactive_keys()
    {
        var clock = new MutableClock(DateTimeOffset.Parse("2026-09-06T12:00:00Z"));
        var transport = new CountingTransport(() => clock.UtcNow.AddMinutes(30));
        var cache = new DingTalkAccessTokenCache(transport, clock);
        await cache.GetOrRefreshAsync("old", "secret", CancellationToken.None);
        clock.UtcNow = clock.UtcNow.AddHours(1);
        await cache.GetOrRefreshAsync("new", "secret", CancellationToken.None);
        // 检查持有的条目数，过期后拒绝命中本身不能证明旧对象已经回收。
        var entries = (System.Collections.IDictionary)typeof(DingTalkAccessTokenCache)
            .GetField("_tokens", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(cache)!;
        Assert.AreEqual(1, entries.Count);
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class CountingTransport(Func<DateTimeOffset> expiresAtUtc) : IDingTalkTransport
    {
        public int TokenRequests { get; private set; }

        public ValueTask<DingTalkAccessToken> GetAccessTokenAsync(
            string appKey,
            string appSecret,
            CancellationToken cancellationToken)
        {
            TokenRequests++;
            return ValueTask.FromResult(new DingTalkAccessToken(
                $"token-{TokenRequests}",
                expiresAtUtc()));
        }

        public ValueTask<string> CreateAndDeliverAsync(
            string accessToken,
            DingTalkCreateAndDeliverCommand command,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(command.OutTrackId);

        public ValueTask<string> CreateProcessInstanceAsync(
            string accessToken,
            DingTalkCreateProcessInstanceCommand command,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult("process-001");

        public ValueTask<DingTalkProcessInstanceSnapshot> GetProcessInstanceAsync(
            string accessToken,
            string processInstanceId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new DingTalkProcessInstanceSnapshot(
                processInstanceId,
                "RUNNING",
                null,
                null));
    }
}
