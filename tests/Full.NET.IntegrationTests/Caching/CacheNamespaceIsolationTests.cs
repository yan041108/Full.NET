using Full.NET.Caching.Abstractions;
using Full.NET.Caching.Fusion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.IntegrationTests.Caching;

/// <summary>真实 Redis 标签隔离：v2 应用/环境命名空间同时隔离 Backplane 和 L2 标签标记。</summary>
[TestClass]
[DoNotParallelize]
public sealed class CacheNamespaceIsolationTests
{
    private const string EntryName = "fixture.namespace";

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Tag_invalidation_reaches_peers_but_preserves_other_application_or_environment(bool differentEnvironment)
    {
        var redis = await SharedDatabaseFixture.GetRedisConnectionStringAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var application = $"namespace-{suffix}";
        var otherApplication = differentEnvironment ? application : $"other-{suffix}";
        var otherEnvironment = differentEnvironment ? "Development" : "Testing";
        await using var primaryProvider = BuildProvider(redis, application, "Testing");
        await using var peerProvider = BuildProvider(redis, application, "Testing");
        await using var isolatedProvider = BuildProvider(redis, otherApplication, otherEnvironment);
        var primary = primaryProvider.GetRequiredService<IFusionCache>();
        var peer = peerProvider.GetRequiredService<IFusionCache>();
        var isolated = isolatedProvider.GetRequiredService<IFusionCache>();
        Assert.AreNotEqual(primary.InstanceId, peer.InstanceId);
        Assert.AreNotEqual(primary.InstanceId, isolated.InstanceId);
        var options = primaryProvider.GetRequiredService<ICachePolicyRegistry>().CreateEntryOptions(EntryName);
        options.AllowBackgroundDistributedCacheOperations = false;
        options.AllowBackgroundBackplaneOperations = false;
        options.ReThrowDistributedCacheExceptions = true;
        options.ReThrowBackplaneExceptions = true;
        using var experiment = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var token = experiment.Token;
        // 故意共用逻辑键，不能靠业务键中的环境前缀掩盖底层命名空间缺失；标签先实际写入。
        var key = $"namespace-memory-{suffix}";
        var sharedKey = $"namespace-shared-{suffix}";
        var tag = $"namespace-tag-{suffix}";
        await primary.SetAsync(key, "owned-old", options, [tag], token);
        await primary.SetAsync(sharedKey, "owned-old", options, [tag], token);
        Assert.AreEqual("owned-old", (await peer.TryGetAsync<string>(key, options, token)).Value);
        await isolated.SetAsync(key, "isolated-old", options, [tag], token);
        await isolated.SetAsync(sharedKey, "isolated-old", options, [tag], token);
        Assert.AreEqual("isolated-old", (await isolated.TryGetAsync<string>(key, options, token)).Value);
        Assert.AreEqual("owned-old", (await peer.TryGetAsync<string>(key, options, token)).Value);
        await DeleteOnlyL2Async(redis, otherApplication, otherEnvironment, key, token);

        await primaryProvider.GetRequiredService<ICacheInvalidator>().RemoveByTagAsync(
            EntryName, tag, CacheInvalidationScope.AllLayersSynchronous, token);
        using var propagation = CancellationTokenSource.CreateLinkedTokenSource(token);
        propagation.CancelAfter(TimeSpan.FromSeconds(10));
        // 同命名空间传播是阳性对照；不能仅凭外部节点没变化就宣称通道隔离正确。
        while ((await peer.TryGetAsync<string>(key, options, propagation.Token)).HasValue)
            await Task.Delay(50, propagation.Token);
        propagation.Token.ThrowIfCancellationRequested();
        Assert.IsFalse((await primary.TryGetAsync<string>(sharedKey, options, token)).HasValue);
        // 该条目的 L2 已不存在；若外部 L1 被错误广播清理，就不能重新从 L2 读回相同值冒充隔离成功。
        var retained = await isolated.TryGetAsync<string>(key, options, token);
        Assert.IsTrue(retained.HasValue, "跨应用/环境标签失效不得清除外部节点已预热的 L1。");
        Assert.AreEqual("isolated-old", retained.Value);

        // 标签标记也有自己的 L1；必须使用全新实例，同时让业务条目和标记实际从 L2 读取。
        await using var coldProvider = BuildProvider(redis, otherApplication, otherEnvironment);
        var cold = coldProvider.GetRequiredService<IFusionCache>();
        Assert.AreNotEqual(isolated.InstanceId, cold.InstanceId);
        var fromL2 = await cold.TryGetAsync<string>(sharedKey, options, token);
        Assert.IsTrue(fromL2.HasValue);
        Assert.AreEqual("isolated-old", fromL2.Value);

        // 反例只复制失效标记，不改业务条目、不广播；第二个冷实例必须识别污染后的 L2 标记。
        await CopyInvalidTagMarkerAsync(redis, application, "Testing", otherApplication, otherEnvironment, tag, token);
        await using var contaminatedProvider = BuildProvider(redis, otherApplication, otherEnvironment);
        var contaminated = contaminatedProvider.GetRequiredService<IFusionCache>();
        Assert.AreNotEqual(cold.InstanceId, contaminated.InstanceId);
        Assert.IsFalse((await contaminated.TryGetAsync<string>(sharedKey, options, token)).HasValue,
            "仅污染 L2 标签标记后，冷实例必须拒绝旧业务条目，不能复用已有实例的本机标签标记。");
    }

    private static async Task CopyInvalidTagMarkerAsync(string connection, string sourceApplication, string sourceEnvironment,
        string targetApplication, string targetEnvironment, string tag, CancellationToken cancellationToken)
    {
        var configuration = ConfigurationOptions.Parse(connection);
        configuration.ConnectTimeout = 1_000;
        configuration.ConnectRetry = 0;
        configuration.AsyncTimeout = 1_000;
        using var redis = await ConnectionMultiplexer.ConnectAsync(configuration);
        cancellationToken.ThrowIfCancellationRequested();
        var sourcePrefix = $"fullnet:{sourceEnvironment.ToLowerInvariant()}:{sourceApplication}:v2:";
        var targetPrefix = $"fullnet:{targetEnvironment.ToLowerInvariant()}:{targetApplication}:v2:";
        var keys = new List<RedisKey>();
        await foreach (var key in redis.GetServer(redis.GetEndPoints().Single())
            .KeysAsync(pattern: $"{sourcePrefix}*{tag}*").WithCancellation(cancellationToken))
            keys.Add(key);
        cancellationToken.ThrowIfCancellationRequested();
        Assert.HasCount(1, keys, "只复制本实验已失效标签的唯一物理标记。");
        var database = redis.GetDatabase();
        var dump = await database.KeyDumpAsync(keys[0]).WaitAsync(cancellationToken);
        Assert.IsNotNull(dump);
        var ttl = await database.KeyTimeToLiveAsync(keys[0]).WaitAsync(cancellationToken);
        Assert.IsTrue(ttl.HasValue);
        var remaining = ttl.GetValueOrDefault();
        Assert.IsGreaterThan(TimeSpan.Zero, remaining);
        var target = targetPrefix + keys[0].ToString()[sourcePrefix.Length..];
        // 目标命名空间也属于本实验；保持 SDK 格式与剩余寿命，不碰其他测试或全局 Redis 配置。
        await database.ExecuteAsync("RESTORE", target, Math.Max(1L, (long)remaining.TotalMilliseconds), dump, "REPLACE")
            .WaitAsync(cancellationToken);
    }

    private static async Task DeleteOnlyL2Async(string connection, string application, string environment,
        string logicalKey, CancellationToken cancellationToken)
    {
        var configuration = ConfigurationOptions.Parse(connection);
        configuration.ConnectTimeout = 1_000;
        configuration.ConnectRetry = 0;
        configuration.AsyncTimeout = 1_000;
        using var redis = await ConnectionMultiplexer.ConnectAsync(configuration);
        cancellationToken.ThrowIfCancellationRequested();
        var prefix = $"fullnet:{environment.ToLowerInvariant()}:{application}:v2:";
        var keys = new List<RedisKey>();
        await foreach (var key in redis.GetServer(redis.GetEndPoints().Single())
            .KeysAsync(pattern: $"{prefix}*{logicalKey}*").WithCancellation(cancellationToken))
            keys.Add(key);
        cancellationToken.ThrowIfCancellationRequested();
        Assert.HasCount(1, keys, "只允许删除本实验专属命名空间的一个物理 L2 条目。");
        Assert.IsTrue(await redis.GetDatabase().KeyDeleteAsync(keys[0]).WaitAsync(cancellationToken));
    }

    private static ServiceProvider BuildProvider(string redis, string application, string environment)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetCaching(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:RedisConnectionString"] = redis,
            ["Cache:ApplicationName"] = application,
            [$"Cache:Entries:{EntryName}:OwnerModule"] = "fixture",
            [$"Cache:Entries:{EntryName}:ConsistencyClass"] = "S1",
            [$"Cache:Entries:{EntryName}:L1Duration"] = "00:05:00",
            [$"Cache:Entries:{EntryName}:L2Duration"] = "00:05:00",
            [$"Cache:Entries:{EntryName}:Jitter"] = "00:00:00",
        }).Build(), environment);
        services.PostConfigure<FusionCacheOptions>(options =>
        {
            // 完成真实订阅后才进入实验，避免把订阅尚未建立的空窗误报为隔离。
            options.WaitForInitialBackplaneSubscribe = true;
            options.EnableAutoRecovery = false;
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
