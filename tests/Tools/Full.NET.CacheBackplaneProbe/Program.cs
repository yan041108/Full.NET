using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Full.NET.Caching.Fusion;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

// 仅由运维显式启动：两个独立进程复用正式注册和通道，所有写入限定在随机验收键，不访问业务库。
if (args.Length != 2 || !Guid.TryParseExact(args[0], "N", out _) || args[1] is not ("a" or "b" or "cleanup")) return 2;
try
{
    return await VerifyAsync(args[0], args[1]);
}
catch (Exception exception)
{
    // 启动绑定、解析和通信失败均不得把连接参数或凭据写入标准错误。
    Console.Error.WriteLine($"缓存双实例验证失败：role={args[1]}; type={exception.GetType().Name}");
    return 1;
}

static async Task<int> VerifyAsync(string run, string role)
{
    var peer = role == "a" ? "b" : "a";
    var prefix = $"fullnet:health:verification:{run}:";
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    var token = deadline.Token;
    var services = new ServiceCollection();
    services.AddLogging(builder => builder.ClearProviders());
    services.AddFullNetCaching(new ConfigurationBuilder().AddEnvironmentVariables().Build(), "Production");
    services.PostConfigure<FusionCacheOptions>(options => options.WaitForInitialBackplaneSubscribe = true);
    await using var provider = services.BuildServiceProvider();
    var cache = provider.GetRequiredService<IFusionCache>();
    var distributed = provider.GetRequiredService<IDistributedCache>();
    if (role == "cleanup")
    {
        foreach (var owner in new[] { "a", "b", "lost" })
            foreach (var phase in new[] { "seeded", "warm", "refilled", "observed", "done", "removed" })
                await distributed.RemoveAsync(prefix + owner + ":" + phase, token);
        Console.WriteLine("本轮临时协调键已清理。");
        return 0;
    }
    var entryOptions = new FusionCacheEntryOptions
    {
        Duration = TimeSpan.FromMinutes(2), JitterMaxDuration = TimeSpan.Zero, Size = 1,
        IsFailSafeEnabled = false, AllowBackgroundDistributedCacheOperations = false,
        AllowBackgroundBackplaneOperations = false, ReThrowBackplaneExceptions = true,
    };
    var memoryOnly = entryOptions.Duplicate();
    memoryOnly.SkipDistributedCacheRead = true;
    memoryOnly.SkipDistributedCacheWrite = true;
    var l2Only = entryOptions.Duplicate();
    l2Only.SkipMemoryCacheRead = true;
    l2Only.SkipMemoryCacheWrite = true;
    var measurements = new List<double>();
    double? missedNotificationConvergenceMilliseconds = null;
    foreach (var owner in new[] { "a", "b" })
    {
        var key = prefix + owner + ":value";
        if (role == owner)
        {
            await cache.SetAsync(key, "v1", entryOptions, token: token);
            await Mark(owner + ":seeded");
            await Wait(owner + ":warm");
            await cache.RemoveAsync(key, entryOptions, token);
            await Wait(owner + ":refilled");
            Require((await cache.TryGetAsync<string>(key, entryOptions, token)).Value == "v2");
            await Mark(owner + ":observed");
        }
        else
        {
            await Wait(owner + ":seeded");
            Require((await cache.TryGetAsync<string>(key, entryOptions, token)).Value == "v1");
            Require((await cache.TryGetAsync<string>(key, memoryOnly, token)).Value == "v1");
            var watch = Stopwatch.StartNew();
            await Mark(owner + ":warm");
            while ((await cache.TryGetAsync<string>(key, memoryOnly, token)).HasValue)
            {
                Require(watch.Elapsed < TimeSpan.FromSeconds(5));
                await Task.Delay(10, token);
            }
            measurements.Add(watch.Elapsed.TotalMilliseconds);
            // 过期为两分钟，此处五秒内 L1 已失效；另核对 L2 删除，排除 TTL 或仅绕过内存的假通过。
            Require(!(await cache.TryGetAsync<string>(key, l2Only, token)).HasValue);
            var factoryCalls = 0;
            var value = await cache.GetOrSetAsync<string>(key, _ =>
            {
                factoryCalls++;
                return Task.FromResult("v2");
            }, options: entryOptions, token: token);
            Require(value == "v2" && factoryCalls == 1);
            await Mark(owner + ":refilled");
            await Wait(owner + ":observed");
        }
    }
    // 只对本轮临时键禁发一次通知，模拟消息丢失；不停止 Redis、不修改现有 ACL 或业务策略。
    var lostKey = prefix + "lost:value";
    var shortLived = entryOptions.Duplicate();
    shortLived.Duration = TimeSpan.FromMilliseconds(1200);
    if (role == "a")
    {
        await cache.SetAsync(lostKey, "old", shortLived, token: token);
        await Mark("lost:seeded");
        await Wait("lost:warm");
        var silent = shortLived.Duplicate();
        silent.SkipBackplaneNotifications = true;
        await cache.RemoveAsync(lostKey, silent, token);
        await Mark("lost:removed");
        await Wait("lost:refilled");
        await cache.RemoveAsync(lostKey, entryOptions, token);
    }
    else
    {
        await Wait("lost:seeded");
        Require((await cache.TryGetAsync<string>(lostKey, shortLived, token)).Value == "old");
        await Mark("lost:warm");
        await Wait("lost:removed");
        Require((await cache.TryGetAsync<string>(lostKey, memoryOnly, token)).Value == "old");
        var convergence = Stopwatch.StartNew();
        while ((await cache.TryGetAsync<string>(lostKey, memoryOnly, token)).HasValue)
        {
            Require(convergence.Elapsed < TimeSpan.FromSeconds(4));
            await Task.Delay(20, token);
        }
        missedNotificationConvergenceMilliseconds = convergence.Elapsed.TotalMilliseconds;
        Require(await cache.GetOrSetAsync<string>(lostKey, _ => Task.FromResult("new"),
            options: shortLived, token: token) == "new");
        await Mark("lost:refilled");
    }
    await Mark(role + ":done");
    await Wait(peer + ":done");
    var health = (await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(token)).Entries["distributed-cache"];
    Require(health.Status == HealthStatus.Healthy);
    // 只删除本进程拥有的验收值，协调标记两分钟后自行过期，避免一方提前删除导致另一方等待超时。
    await cache.RemoveAsync(prefix + role + ":value", entryOptions, token);
    Console.WriteLine(JsonSerializer.Serialize(new { role, processId = Environment.ProcessId, instance = cache.InstanceId,
        node = Environment.GetEnvironmentVariable("NODE_NAME"), l1TtlSeconds = 120,
        receivedInvalidationMilliseconds = measurements, l2Removal = true, authoritativeRefill = true,
        missedNotificationConvergenceMilliseconds, health = health.Status.ToString(), run }));
    return 0;

    async Task Mark(string name) => await distributed.SetAsync(prefix + name, Encoding.UTF8.GetBytes("ready"),
        new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2) }, token);
    async Task Wait(string name)
    {
        while (await distributed.GetAsync(prefix + name, token) is null) await Task.Delay(20, token);
    }
    static void Require(bool success) { if (!success) throw new InvalidOperationException("验收断言失败"); }
}
