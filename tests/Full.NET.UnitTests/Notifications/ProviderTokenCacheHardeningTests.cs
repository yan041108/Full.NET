using Full.NET.Abstractions.Time;
using Full.NET.Modules.Notifications.Providers.WeCom;
using Full.NET.Modules.Notifications.Providers.DingTalk;
using Full.NET.Modules.Notifications.Providers.WeChatMiniProgram;
using NSubstitute;

namespace Full.NET.UnitTests.Notifications;

/// <summary>凭据轮换不得复用旧令牌；并发刷新合并后，等待方取消不得影响共享刷新结果。</summary>
[TestClass]
public sealed class ProviderTokenCacheHardeningTests
{
    [TestMethod]
    [DataRow("wecom")]
    [DataRow("dingtalk")]
    [DataRow("mini")]
    public async Task Late_old_refresh_cannot_replace_rotated_credential(string provider)
    {
        var old = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = CreateClient(provider, (secret, token) => secret == "old"
            ? old.Task.WaitAsync(token) : Task.FromResult("new-token"));
        var pending = client("app", "old", CancellationToken.None).AsTask();
        Assert.AreEqual("new-token", await client("app", "new", CancellationToken.None));
        old.SetResult("old-token");
        await pending;
        Assert.AreEqual("new-token", await client("app", "new", CancellationToken.None));
    }

    [TestMethod]
    [DataRow("wecom")]
    [DataRow("dingtalk")]
    [DataRow("mini")]
    public async Task Failed_refresh_is_not_cached_and_can_retry(string provider)
    {
        var calls = 0;
        var client = CreateClient(provider, (_, _) => Interlocked.Increment(ref calls) == 1
            ? Task.FromException<string>(new InvalidOperationException("unavailable")) : Task.FromResult("recovered"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client("app", "secret", CancellationToken.None).AsTask());
        Assert.AreEqual("recovered", await client("app", "secret", CancellationToken.None));
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    public async Task Shutdown_cancels_and_drains_owned_refresh()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var cache = new Full.NET.Modules.Notifications.Providers.ProviderTokenRefreshCache(clock);
        var pending = cache.GetAsync("app", "secret", async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return ("unused", clock.UtcNow.AddHours(1));
        }, CancellationToken.None).AsTask();
        await cache.DisposeAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
    }

    [TestMethod]
    public async Task Pending_refreshes_are_bounded_and_not_evicted_to_start_more_work()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var cache = new Full.NET.Modules.Notifications.Providers.ProviderTokenRefreshCache(clock);
        var calls = 0;
        async ValueTask<(string, DateTimeOffset)> Refresh(CancellationToken token)
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(Timeout.Infinite, token);
            return ("unused", clock.UtcNow.AddHours(1));
        }
        var pending = Enumerable.Range(0, 1024)
            .Select(index => cache.GetAsync($"app-{index}", "secret", Refresh, CancellationToken.None).AsTask()).ToArray();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                cache.GetAsync("overflow", "secret", Refresh, CancellationToken.None).AsTask());
            Assert.AreEqual(1024, calls);
            Assert.AreEqual(1024, cache.Count);
        }
        finally { await cache.DisposeAsync(); }
        foreach (var task in pending) await Assert.ThrowsAsync<OperationCanceledException>(() => task);
    }

    [TestMethod]
    [DataRow("wecom")]
    [DataRow("dingtalk")]
    [DataRow("mini")]
    public async Task Rotation_never_reuses_previous_credential_token(string provider)
    {
        var client = CreateClient(provider, (secret, _) => Task.FromResult($"issued-{secret}"));
        Assert.AreEqual("issued-v1", await client("app", "v1", CancellationToken.None));
        Assert.AreEqual("issued-v2", await client("app", "v2", CancellationToken.None));
    }

    [TestMethod]
    [DataRow("wecom")]
    [DataRow("dingtalk")]
    [DataRow("mini")]
    public async Task Concurrent_refresh_is_coalesced_and_waiter_cancellation_is_independent(string provider)
    {
        var issued = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var client = CreateClient(provider, async (_, token) =>
        {
            Interlocked.Increment(ref calls);
            return await issued.Task.WaitAsync(token);
        });
        using var cancelled = new CancellationTokenSource();
        var first = client("app", "v1", cancelled.Token).AsTask();
        var second = client("app", "v1", CancellationToken.None).AsTask();
        cancelled.Cancel();
        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => first);
            Assert.AreEqual(1, calls);
        }
        finally { issued.TrySetResult("shared-token"); }
        Assert.AreEqual("shared-token", await second);
        Assert.AreEqual("shared-token", await client("app", "v1", CancellationToken.None));
    }

    private static Func<string, string, CancellationToken, ValueTask<string>> CreateClient(
        string provider, Func<string, CancellationToken, Task<string>> factory)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var expiry = clock.UtcNow.AddHours(1);
        var transport = new Transport(factory, expiry);
        return provider switch
        {
            "wecom" => new WeComAccessTokenCache(transport, clock).GetOrRefreshAsync,
            "dingtalk" => new DingTalkAccessTokenCache(transport, clock).GetOrRefreshAsync,
            _ => new WeChatMiniProgramAccessTokenCache(transport, clock).GetOrRefreshAsync,
        };
    }

    private sealed class Transport(Func<string, CancellationToken, Task<string>> factory, DateTimeOffset expiry)
        : IWeComTransport, IDingTalkTransport, IWeChatMiniProgramTransport
    {
        async ValueTask<WeComAccessToken> IWeComTransport.GetAccessTokenAsync(string id, string secret, CancellationToken token) => new(await factory(secret, token), expiry);
        async ValueTask<DingTalkAccessToken> IDingTalkTransport.GetAccessTokenAsync(string id, string secret, CancellationToken token) => new(await factory(secret, token), expiry);
        async ValueTask<WeChatMiniProgramAccessToken> IWeChatMiniProgramTransport.GetAccessTokenAsync(string id, string secret, CancellationToken token) => new(await factory(secret, token), expiry);
        public ValueTask<string> SendTextAsync(string token, WeComSendTextCommand command, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<string> CreateAndDeliverAsync(string token, DingTalkCreateAndDeliverCommand command, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<string> CreateProcessInstanceAsync(string token, DingTalkCreateProcessInstanceCommand command, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<DingTalkProcessInstanceSnapshot> GetProcessInstanceAsync(string token, string id, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<WeChatMiniProgramSession> ExchangeJsCodeAsync(string id, string secret, string code, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<string> SendSubscribeMessageAsync(string token, WeChatMiniProgramSubscribeSendCommand command, CancellationToken ct) => throw new NotSupportedException();
    }
}
