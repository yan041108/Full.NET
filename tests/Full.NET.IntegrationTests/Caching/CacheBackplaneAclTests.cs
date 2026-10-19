using Full.NET.Caching.Fusion;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.IntegrationTests.Caching;

/// <summary>真实 Redis ACL 回归：GET 可用时发布/订阅拒绝仍须降级，恢复必须有实际通信证据。</summary>
[TestClass]
[DoNotParallelize]
public sealed class CacheBackplaneAclTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Backplane_permission_failure_is_not_hidden_by_L2_GET_and_actual_recovery_is_observed(bool denySubscription)
    {
        var redis = await SharedDatabaseFixture.GetRedisConnectionStringAsync();
        var adminOptions = ConfigurationOptions.Parse(redis);
        adminOptions.AllowAdmin = true;
        using var admin = await ConnectionMultiplexer.ConnectAsync(adminOptions);
        var suffix = Guid.NewGuid().ToString("N");
        var user = $"fullnet-cache-acl-{suffix}";
        var password = Guid.NewGuid().ToString("N");
        ServiceProvider? provider = null;
        try
        {
            // 仅创建临时用户，不触碰默认账户、全局通知设置或其他测试的连接。
            var rules = new List<object> { "SETUSER", user, "reset", "on", $">{password}", "~*", "&*", "+@all" };
            rules.Add(denySubscription ? "-subscribe" : "-publish");
            if (denySubscription) { rules.Add("-psubscribe"); rules.Add("-ssubscribe"); }
            await admin.GetDatabase().ExecuteAsync("ACL", rules.ToArray());
            var clientOptions = ConfigurationOptions.Parse(redis);
            clientOptions.User = user;
            clientOptions.Password = password;
            provider = BuildProvider(clientOptions.ToString(includePassword: true), suffix);
            var distributed = provider.GetRequiredService<IDistributedCache>();
            Assert.IsNull(await distributed.GetAsync($"acl-missing-{suffix}"));
            var cache = provider.GetRequiredService<IFusionCache>();
            var key = $"acl-value-{suffix}";
            await cache.SetAsync(key, "value", EntryOptions());
            var failed = await WaitForFailureAsync(provider);
            Assert.AreEqual(HealthStatus.Degraded, failed.Status);
            Assert.AreEqual(HealthStatus.Unhealthy, (await CheckAsync(provider)).Status);
            Assert.DoesNotContain(password, failed.Description ?? "");
            Assert.DoesNotContain(user, failed.Description ?? "");
            Assert.IsNull(await distributed.GetAsync($"acl-missing-{suffix}"));
            // 实验已等待初始订阅结束；不靠可能在监听注册前发生的单次事件判断重试完成。
            await admin.GetDatabase().ExecuteAsync("ACL", "SETUSER", user,
                "+publish", "+subscribe", "+psubscribe", "+ssubscribe");
            Assert.AreEqual(HealthStatus.Unhealthy, (await CheckAsync(provider)).Status);
            if (denySubscription)
            {
                // 订阅拒绝后的恢复采用新宿主注册周期，不宣称仅修改 ACL 就自动重新订阅。
                await provider.DisposeAsync();
                provider = BuildProvider(clientOptions.ToString(includePassword: true), suffix);
                cache = provider.GetRequiredService<IFusionCache>();
            }
            await WaitForHealthyAsync(provider, cache, key);
        }
        finally
        {
            try { if (provider is not null) await provider.DisposeAsync(); }
            finally { await admin.GetDatabase().ExecuteAsync("ACL", "DELUSER", user); }
        }
    }

    private static ServiceProvider BuildProvider(string redis, string application)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetCaching(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:RedisConnectionString"] = redis,
            ["Cache:ApplicationName"] = application,
        }).Build(), "Testing");
        services.PostConfigure<FusionCacheOptions>(options =>
        {
            // 等待初始订阅、缩短实验等待并排除自动重放；生产注册和恢复预算保持原值。
            options.WaitForInitialBackplaneSubscribe = true;
            options.BackplaneCircuitBreakerDuration = TimeSpan.FromMilliseconds(200);
            options.EnableAutoRecovery = false;
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static FusionCacheEntryOptions EntryOptions() => new()
    {
        Duration = TimeSpan.FromSeconds(2),
        JitterMaxDuration = TimeSpan.Zero,
        IsFailSafeEnabled = false,
        AllowBackgroundBackplaneOperations = false,
        AllowBackgroundDistributedCacheOperations = false,
        ReThrowBackplaneExceptions = false,
    };

    private static async Task<HealthReportEntry> CheckAsync(ServiceProvider provider) =>
        (await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync()).Entries["distributed-cache"];

    private static async Task<HealthReportEntry> WaitForFailureAsync(ServiceProvider provider)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            var entry = await CheckAsync(provider);
            if (entry.Description?.Contains("Backplane 发布或订阅失败", StringComparison.Ordinal) == true) return entry;
            await Task.Delay(25, deadline.Token);
        }
    }

    private static async Task WaitForHealthyAsync(ServiceProvider provider, IFusionCache cache, string key)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            await cache.SetAsync(key, "recovered", EntryOptions(), token: deadline.Token);
            if ((await CheckAsync(provider)).Status == HealthStatus.Healthy) return;
            await Task.Delay(25, deadline.Token);
        }
    }
}
