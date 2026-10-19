using Full.NET.Caching.Fusion;
using Full.NET.Caching.Fusion.Health;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane;

namespace Full.NET.UnitTests.Caching;

/// <summary>使用真实 SDK 与 Backplane 故障替身，验证 L2 读成功不能覆盖发布/订阅失败。</summary>
[TestClass]
[DoNotParallelize]
public sealed class CacheBackplaneHealthTests
{
    [TestMethod]
    public async Task Unknown_backplane_is_degraded_and_health_probe_does_not_publish_or_subscribe()
    {
        var backplane = new TestBackplane();
        await using var provider = CreateProvider(backplane);
        Assert.AreEqual(HealthStatus.Degraded, await CheckAsync(provider));
        Assert.AreEqual(0, backplane.Subscriptions);
        Assert.AreEqual(0, backplane.Publications);
    }

    [TestMethod]
    public async Task Publication_denied_with_healthy_GET_stays_failed_until_actual_publication_recovers()
    {
        var backplane = new TestBackplane { PublishFailure = new RedisServerException("NOPERM fixture-secret") };
        await using var provider = CreateProvider(backplane);
        var cache = provider.GetRequiredService<IFusionCache>();
        await backplane.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await cache.SetAsync("health-publish", "value", SynchronousOptions());
        Assert.AreEqual(HealthStatus.Degraded, await CheckAsync(provider));
        var failed = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        Assert.AreEqual(HealthStatus.Unhealthy, failed.Entries["distributed-cache"].Status);
        Assert.DoesNotContain("fixture-secret", failed.Entries["distributed-cache"].Description ?? "");

        // 重连与收到消息都不能证明发布权限恢复；SDK 收到有效消息可重闭合熔断器，但失败事实仍须保留。
        backplane.PublishFailure = null;
        await backplane.ConnectionAsync(new BackplaneConnectionInfo(true));
        await backplane.ReceiveAsync(BackplaneMessage.CreateForEntryRemove("remote-node", "health-other", DateTime.UtcNow.Ticks));
        Assert.AreEqual(HealthStatus.Unhealthy, await CheckAsync(provider));
        await cache.SetAsync("health-publish", "new-value", SynchronousOptions());
        Assert.AreEqual(HealthStatus.Healthy, await CheckAsync(provider));
    }

    [TestMethod]
    public async Task Successful_publication_cannot_hide_a_denied_subscription()
    {
        var backplane = new TestBackplane { SubscribeFailure = new RedisServerException("NOPERM fixture-subscribe") };
        await using var provider = CreateProvider(backplane);
        var cache = provider.GetRequiredService<IFusionCache>();
        await backplane.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await cache.SetAsync("health-subscription", "value", SynchronousOptions());
        Assert.AreEqual(HealthStatus.Degraded, await CheckAsync(provider));
        Assert.AreEqual(HealthStatus.Unhealthy, await CheckAsync(provider));
    }

    [TestMethod]
    public async Task Successful_subscription_and_publication_are_healthy_but_reconnection_needs_new_evidence()
    {
        var backplane = new TestBackplane();
        await using var provider = CreateProvider(backplane);
        var cache = provider.GetRequiredService<IFusionCache>();
        await backplane.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var message = BackplaneMessage.CreateForEntryRemove("remote-node", "health-other", DateTime.UtcNow.Ticks);
        await backplane.ReceiveAsync(message);
        await cache.SetAsync("health-ready", "value", SynchronousOptions());
        Assert.AreEqual(HealthStatus.Healthy, await CheckAsync(provider));
        await backplane.ConnectionAsync(new BackplaneConnectionInfo(true));
        Assert.AreEqual(HealthStatus.Degraded, await CheckAsync(provider));
        await cache.SetAsync("health-ready", "new-value", SynchronousOptions());
        Assert.AreEqual(HealthStatus.Degraded, await CheckAsync(provider));
        await backplane.ReceiveAsync(message);
        Assert.AreEqual(HealthStatus.Healthy, await CheckAsync(provider));
    }

    private static FusionCacheEntryOptions SynchronousOptions() => new()
    {
        AllowBackgroundBackplaneOperations = false,
        AllowBackgroundDistributedCacheOperations = false,
        ReThrowBackplaneExceptions = false,
    };

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Caller_cancellation_is_preserved_without_marking_backplane_failed(bool asynchronous)
    {
        var state = new CacheBackplaneHealthState();
        var inner = new TestBackplane();
        using var observed = new ObservedFusionCacheBackplane(inner, state);
        observed.Subscribe(SubscriptionOptions());
        var message = BackplaneMessage.CreateForEntryRemove("source", "key", DateTime.UtcNow.Ticks);
        observed.Publish(message, SynchronousOptions());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            if (asynchronous) await observed.PublishAsync(message, SynchronousOptions(), cancellation.Token);
            else observed.Publish(message, SynchronousOptions(), cancellation.Token);
            Assert.Fail("已取消的调用应传播取消异常。");
        }
        catch (OperationCanceledException error) { Assert.AreEqual(cancellation.Token, error.CancellationToken); }
        Assert.AreEqual(CacheBackplaneStatus.Operational, state.Status);
    }

    [TestMethod]
    public async Task Late_publication_success_cannot_erase_a_newer_failure()
    {
        var state = new CacheBackplaneHealthState();
        var inner = new TestBackplane();
        using var observed = new ObservedFusionCacheBackplane(inner, state);
        observed.Subscribe(SubscriptionOptions());
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        inner.PublishOverride = () => new ValueTask(gate.Task);
        var message = BackplaneMessage.CreateForEntryRemove("source", "key", DateTime.UtcNow.Ticks);
        var older = observed.PublishAsync(message, SynchronousOptions());
        var original = new RedisServerException("NOPERM");
        inner.PublishFailure = original;
        try { observed.Publish(message, SynchronousOptions()); Assert.Fail("拒绝必须继续传播。"); }
        catch (RedisServerException error) { Assert.AreSame(original, error); }
        gate.SetResult();
        await older;
        Assert.AreEqual(CacheBackplaneStatus.Failed, state.Status);
    }

    [TestMethod]
    public async Task Reconnection_discards_success_from_an_older_in_flight_publication()
    {
        var state = new CacheBackplaneHealthState();
        var inner = new TestBackplane();
        using var observed = new ObservedFusionCacheBackplane(inner, state);
        await observed.SubscribeAsync(SubscriptionOptions());
        var message = BackplaneMessage.CreateForEntryRemove("source", "key", DateTime.UtcNow.Ticks);
        observed.Publish(message, SynchronousOptions());
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        inner.PublishOverride = () => new ValueTask(gate.Task);
        var older = observed.PublishAsync(message, SynchronousOptions());
        await inner.ConnectionAsync(new BackplaneConnectionInfo(true));
        gate.SetResult();
        await older;
        await inner.ReceiveAsync(message);
        Assert.AreEqual(CacheBackplaneStatus.Unknown, state.Status);
        observed.Publish(message, SynchronousOptions());
        Assert.AreEqual(CacheBackplaneStatus.Operational, state.Status);
    }

    [TestMethod]
    public async Task Retired_instance_callbacks_and_completions_cannot_change_current_health()
    {
        var state = new CacheBackplaneHealthState();
        var oldInner = new TestBackplane();
        using var oldObserved = new ObservedFusionCacheBackplane(oldInner, state);
        oldObserved.Subscribe(SubscriptionOptions());
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        oldInner.PublishOverride = () => new ValueTask(gate.Task);
        var message = BackplaneMessage.CreateForEntryRemove("source", "key", DateTime.UtcNow.Ticks);
        var pending = oldObserved.PublishAsync(message, SynchronousOptions());
        oldObserved.Dispose();
        Assert.AreEqual(CacheBackplaneStatus.Unknown, state.Status);
        var currentInner = new TestBackplane { SubscribeFailure = new RedisServerException("NOPERM") };
        using var current = new ObservedFusionCacheBackplane(currentInner, state);
        try { current.Subscribe(SubscriptionOptions()); Assert.Fail("订阅拒绝应传播。"); }
        catch (RedisServerException) { }
        current.Publish(message, SynchronousOptions());
        await oldInner.ConnectionAsync(new BackplaneConnectionInfo(true));
        await oldInner.ReceiveAsync(message);
        gate.SetResult();
        await pending;
        Assert.AreEqual(CacheBackplaneStatus.Failed, state.Status);
    }

    [TestMethod]
    public async Task Subscription_callbacks_preserve_SDK_handlers_and_invalid_messages_are_not_recovery()
    {
        var state = new CacheBackplaneHealthState();
        var inner = new TestBackplane { SubscribeFailure = new RedisServerException("NOPERM") };
        using var observed = new ObservedFusionCacheBackplane(inner, state);
        var connections = 0;
        var messages = 0;
        var options = new BackplaneSubscriptionOptions("cache", "instance", "channel",
            _ => connections++, _ => messages++,
            _ => { connections++; return ValueTask.CompletedTask; },
            _ => { messages++; return ValueTask.CompletedTask; });
        try { await observed.SubscribeAsync(options); Assert.Fail("订阅拒绝应传播。"); }
        catch (RedisServerException) { }
        var message = BackplaneMessage.CreateForEntryRemove("source", "key", DateTime.UtcNow.Ticks);
        observed.Publish(message, SynchronousOptions());
        await inner.ConnectionAsync(new BackplaneConnectionInfo(false));
        inner.Connection(new BackplaneConnectionInfo(false));
        await inner.ReceiveAsync(new BackplaneMessage());
        inner.Receive(new BackplaneMessage());
        Assert.AreEqual(2, connections);
        Assert.AreEqual(2, messages);
        Assert.AreEqual(CacheBackplaneStatus.Failed, state.Status);
        Assert.AreEqual(options.CacheName, inner.Subscription!.CacheName);
        Assert.AreEqual(options.CacheInstanceId, inner.Subscription.CacheInstanceId);
        Assert.AreEqual(options.ChannelName, inner.Subscription.ChannelName);
        inner.Receive(message);
        Assert.AreEqual(CacheBackplaneStatus.Operational, state.Status);
    }

    private static BackplaneSubscriptionOptions SubscriptionOptions() => new("cache", "instance", "channel",
        _ => { }, _ => { }, _ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Resubscribing_same_instance_observes_new_failures_and_ignores_retired_cycle(bool asynchronous)
    {
        var state = new CacheBackplaneHealthState();
        var inner = new TestBackplane();
        using var observed = new ObservedFusionCacheBackplane(inner, state);
        observed.Subscribe(SubscriptionOptions());
        var previous = inner.Subscription!;
        var message = BackplaneMessage.CreateForEntryRemove("source", "key", DateTime.UtcNow.Ticks);
        observed.Publish(message, SynchronousOptions());
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        inner.PublishOverride = () => new ValueTask(gate.Task);
        var pending = observed.PublishAsync(message, SynchronousOptions());
        if (asynchronous) await observed.UnsubscribeAsync();
        else observed.Unsubscribe();
        Assert.AreEqual(CacheBackplaneStatus.Unknown, state.Status);
        inner.SubscribeFailure = new RedisServerException("NOPERM");
        try
        {
            if (asynchronous) await observed.SubscribeAsync(SubscriptionOptions());
            else observed.Subscribe(SubscriptionOptions());
            Assert.Fail("新订阅周期必须传播拒绝。");
        }
        catch (RedisServerException) { }
        observed.Publish(message, SynchronousOptions());
        Assert.AreEqual(CacheBackplaneStatus.Failed, state.Status);
        await previous.ConnectHandlerAsync!(new BackplaneConnectionInfo(true));
        await previous.IncomingMessageHandlerAsync!(message);
        gate.SetResult();
        await pending;
        Assert.AreEqual(CacheBackplaneStatus.Failed, state.Status);
        inner.SubscribeFailure = null;
        if (asynchronous) await observed.SubscribeAsync(SubscriptionOptions());
        else observed.Subscribe(SubscriptionOptions());
        Assert.AreEqual(CacheBackplaneStatus.Operational, state.Status);
    }

    private static ServiceProvider CreateProvider(TestBackplane backplane)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetCaching(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Cache:RedisConnectionString"] = "127.0.0.1:1,connectRetry=0" }).Build(), "Testing");
        var distributed = Substitute.For<IDistributedCache>();
        distributed.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<byte[]?>(null));
        services.AddSingleton(distributed);
        services.AddSingleton<IFusionCacheBackplane>(backplane);
        return services.BuildServiceProvider();
    }

    private static async Task<HealthStatus> CheckAsync(ServiceProvider provider) =>
        (await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync()).Entries["distributed-cache"].Status;

    private sealed class TestBackplane : IFusionCacheBackplane
    {
        private BackplaneSubscriptionOptions? _options;
        public Exception? SubscribeFailure { get; set; }
        public Exception? PublishFailure { get; set; }
        public Func<ValueTask>? PublishOverride { get; set; }
        public BackplaneSubscriptionOptions? Subscription => _options;
        public int Subscriptions { get; private set; }
        public int Publications { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Subscribe(BackplaneSubscriptionOptions options)
        {
            _options = options;
            Subscriptions++;
            Started.TrySetResult();
            if (SubscribeFailure is not null) throw SubscribeFailure;
        }
        public ValueTask SubscribeAsync(BackplaneSubscriptionOptions options)
        { Subscribe(options); return ValueTask.CompletedTask; }
        public void Unsubscribe() { }
        public ValueTask UnsubscribeAsync() => ValueTask.CompletedTask;
        public void Publish(BackplaneMessage message, FusionCacheEntryOptions options, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            Publications++;
            if (PublishFailure is not null) throw PublishFailure;
        }
        public ValueTask PublishAsync(BackplaneMessage message, FusionCacheEntryOptions options, CancellationToken token = default)
        { Publish(message, options, token); return PublishOverride?.Invoke() ?? ValueTask.CompletedTask; }
        public void Connection(BackplaneConnectionInfo info) => _options!.ConnectHandler!(info);
        public void Receive(BackplaneMessage message) => _options!.IncomingMessageHandler!(message);
        public ValueTask ConnectionAsync(BackplaneConnectionInfo info) => _options!.ConnectHandlerAsync!(info);
        public ValueTask ReceiveAsync(BackplaneMessage message) => _options!.IncomingMessageHandlerAsync!(message);
        public void Dispose() { }
    }
}
