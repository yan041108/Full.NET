using Full.NET.Caching.Fusion;
using Full.NET.Caching.Abstractions;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Settings.Catalogs;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Features.ManageMyGridPreferences;
using ZiggyCreatures.Caching.Fusion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Full.NET.Modules.Settings.Persistence;
using Full.NET.Modules.Settings;
using Full.NET.Modules.Settings.Serialization;
using System.Text.Json;
using System.Globalization;
using Microsoft.Extensions.Caching.Distributed;

namespace Full.NET.UnitTests.Settings;

/// <summary>单元测试（设置）：Grid Preference。</summary>
[TestClass]
public sealed class GridPreferenceTests
{
    [TestMethod]
    public async Task Oversized_raw_L2_input_is_rejected_before_parse_and_grid_reads_authority()
    {
        await using var provider = CreatePayloadProvider(65_536, maxDeserializationBytes: 1);
        var userId = Guid.NewGuid();
        var key = CacheKeyBuilder.ForGlobal("Testing", "settings", "grid-preference", $"{userId:N}:identity.users", "v2");
        await provider.GetRequiredService<IFusionCache>().SetAsync(key,
            new GridPreferenceResponse("identity.users", 2, [], 777));
        await provider.GetRequiredService<ICacheInvalidator>().RemoveAsync(CacheEntryNames.GridPreference,
            key, CacheInvalidationScope.CurrentNodeOnly);
        var query = Substitute.For<IQueryExecutor>();
        var result = await CreatePayloadService(provider, query).GetAsync(userId, "identity.users");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(0, result.Value!.Version);
        await query.Received(1).QuerySingleOrDefaultAsync<GridPreferenceRecord>(
            Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Late_grid_read_after_committed_delete_can_refill_both_nodes_but_expires()
    {
        await using var primary = CreatePayloadProvider(65_536, TimeSpan.FromSeconds(1));
        await using var secondary = CreatePayloadProvider(65_536, TimeSpan.FromSeconds(1),
            primary.GetRequiredService<IDistributedCache>());
        Assert.AreNotEqual(primary.GetRequiredService<IFusionCache>().InstanceId,
            secondary.GetRequiredService<IFusionCache>().InstanceId);
        foreach (var provider in new[] { primary, secondary })
        {
            var policy = provider.GetRequiredService<ICachePolicyRegistry>().GetRequired(CacheEntryNames.GridPreference);
            Assert.AreEqual(TimeSpan.FromSeconds(1), policy.L1Duration);
            Assert.AreEqual(TimeSpan.FromSeconds(1), policy.L2Duration);
            Assert.AreEqual(TimeSpan.Zero, policy.Jitter);
            Assert.IsFalse(policy.FailSafeEnabled);
        }
        var query = Substitute.For<IQueryExecutor>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<GridPreferenceRecord?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        query.QuerySingleOrDefaultAsync<GridPreferenceRecord>(Arg.Any<SqlStatement>(),
                Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref calls) != 1)
                    return Task.FromResult<GridPreferenceRecord?>(null);
                started.TrySetResult();
                return release.Task;
            });
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var first = CreatePayloadService(primary, query, command);
        var second = CreatePayloadService(secondary, query);
        var userId = Guid.NewGuid();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pending = first.GetAsync(userId, "identity.users", timeout.Token);
        try
        {
            await started.Task.WaitAsync(timeout.Token);
            Assert.IsTrue((await first.DeleteAsync(userId, "identity.users", timeout.Token)).IsSuccess);
            await command.Received(1).ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());

            // gate 保证旧快照在事务返回、两次失效都结束后才交还工厂，不靠 sleep 制造竞态。
            release.SetResult(new GridPreferenceRecord
            {
                UserId = userId, GridKey = "identity.users", SchemaVersion = 2, Version = 7,
            });
            Assert.AreEqual(7, (await pending).Value!.Version);
            Assert.AreEqual(7, (await first.GetAsync(userId, "identity.users", timeout.Token)).Value!.Version);
            Assert.AreEqual(7, (await second.GetAsync(userId, "identity.users", timeout.Token)).Value!.Version);
            Assert.AreEqual(1, Volatile.Read(ref calls), "第二实例确实从共享 L2 取到迟到旧值。");

            // 时间等待只用于检验过期收敛；短 TTL、零抖动和关闭 Fail-Safe 是本实验的明确前提。
            while ((await second.GetAsync(userId, "identity.users", timeout.Token)).Value!.Version != 0)
                await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
            // 各节点的本地过期并不同时发生；另一节点回源成功不能替代本节点的收敛检查。
            while ((await first.GetAsync(userId, "identity.users", timeout.Token)).Value!.Version != 0)
                await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
            Assert.AreEqual(0, (await first.GetAsync(userId, "identity.users", timeout.Token)).Value!.Version);
        }
        finally
        {
            release.TrySetCanceled();
            try { await pending; } catch (OperationCanceledException) { }
        }
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(1)]
    public async Task Payload_budget_boundary_applies_to_both_cache_layers(int budgetOffset)
    {
        var expected = GridPreferencePolicy.Default(GridPreferenceCatalog.GetRequired("identity.users"));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(expected,
            SettingsJsonSerializerContext.Default.GridPreferenceResponse).Length;
        await using var provider = CreatePayloadProvider(bytes + budgetOffset);
        Assert.IsTrue(provider.GetRequiredService<IFusionCache>().HasDistributedCache);
        var query = Substitute.For<IQueryExecutor>();
        var service = CreatePayloadService(provider, query);
        var userId = Guid.NewGuid();

        Assert.AreEqual(0, (await service.GetAsync(userId, "identity.users")).Value!.Version);
        Assert.AreEqual(0, (await service.GetAsync(userId, "identity.users")).Value!.Version);
        await query.Received(budgetOffset < 0 ? 2 : 1).QuerySingleOrDefaultAsync<GridPreferenceRecord>(
            Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());

        // 排空当前节点后再次读，证明合规值实际进入 L2，超限值没有借 L2 绕过预算。
        await provider.GetRequiredService<ICacheInvalidator>().RemoveAsync(CacheEntryNames.GridPreference,
            CacheKeyBuilder.ForGlobal("Testing", "settings", "grid-preference", $"{userId:N}:identity.users", "v2"),
            CacheInvalidationScope.CurrentNodeOnly);
        Assert.AreEqual(0, (await service.GetAsync(userId, "identity.users")).Value!.Version);
        await query.Received(budgetOffset < 0 ? 3 : 1).QuerySingleOrDefaultAsync<GridPreferenceRecord>(
            Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Escaped_unicode_legacy_payload_cannot_bypass_serialized_byte_budget()
    {
        // 模拟旧节点写入的非规范内容；默认 JSON 编码器会转义中文和代理项，预算必须计算编码后的大小。
        var legacy = new GridPreferenceResponse("identity.users", 2,
            [new GridColumnPreference(new string('中', 100) + "😀", 0, 120, true, null)], 777);
        var relaxedJson = JsonSerializer.Serialize(legacy, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        var actualBytes = JsonSerializer.SerializeToUtf8Bytes(legacy,
            SettingsJsonSerializerContext.Default.GridPreferenceResponse).Length;
        Assert.IsGreaterThan(relaxedJson.Length, actualBytes);
        await using var provider = CreatePayloadProvider(relaxedJson.Length);
        var userId = Guid.NewGuid();
        await provider.GetRequiredService<IFusionCache>().SetAsync(
            CacheKeyBuilder.ForGlobal("Testing", "settings", "grid-preference", $"{userId:N}:identity.users", "v2"),
            legacy, provider.GetRequiredService<ICachePolicyRegistry>().CreateEntryOptions(CacheEntryNames.GridPreference));
        var query = Substitute.For<IQueryExecutor>();
        var service = CreatePayloadService(provider, query);

        Assert.AreEqual(0, (await service.GetAsync(userId, "identity.users")).Value!.Version);
        await query.Received(1).QuerySingleOrDefaultAsync<GridPreferenceRecord>(
            Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    private static ServiceProvider CreatePayloadProvider(int budget, TimeSpan? duration = null,
        IDistributedCache? shared = null, int maxDeserializationBytes = 1_048_576)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDistributedMemoryCache();
        if (shared is not null)
            services.AddSingleton(shared);
        services.AddFullNetCaching(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:MaxDeserializationBytes"] = maxDeserializationBytes.ToString(CultureInfo.InvariantCulture),
            ["Cache:Entries:settings.grid-preference:OwnerModule"] = "settings",
            ["Cache:Entries:settings.grid-preference:ConsistencyClass"] = "S2",
            ["Cache:Entries:settings.grid-preference:L1Duration"] = (duration ?? TimeSpan.FromMinutes(1)).ToString("c", CultureInfo.InvariantCulture),
            ["Cache:Entries:settings.grid-preference:L2Duration"] = (duration ?? TimeSpan.FromMinutes(15)).ToString("c", CultureInfo.InvariantCulture),
            ["Cache:Entries:settings.grid-preference:Jitter"] = "00:00:00",
            ["Cache:Entries:settings.grid-preference:MaxSerializedBytes"] = budget.ToString(CultureInfo.InvariantCulture),
        }).Build(), "Testing");
        // SDK 默认忽略内存分布式替身；这里只为两层行为测试显式启用，生产仍使用 Redis。
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IFusionCache>().SetupDistributedCache(provider.GetRequiredService<IDistributedCache>());
        return provider;
    }

    private static MyGridPreferenceService CreatePayloadService(ServiceProvider provider, IQueryExecutor query,
        ICommandExecutor? command = null)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Testing");
        return new MyGridPreferenceService(query, command ?? Substitute.For<ICommandExecutor>(), new PassThroughTransaction(),
            provider.GetRequiredService<IFusionCache>(), provider.GetRequiredService<ICachePolicyRegistry>(),
            environment, Substitute.For<IClock>(), Substitute.For<IIdGenerator>(),
            provider.GetRequiredService<ICacheInvalidator>(), provider.GetRequiredService<IOptions<CacheOptions>>(),
            NullLogger<MyGridPreferenceService>.Instance);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Owner_port_uses_registered_schema_and_requested_scope(bool includeSharedCache)
    {
        var cache = Substitute.For<ICacheInvalidator>();
        await using var provider = CreateOwnerProvider(cache);
        var owner = provider.GetRequiredService<IGridPreferenceCacheInvalidator>();
        var userId = Guid.NewGuid();
        var definition = GridPreferenceCatalog.GetRequired("identity.users");
        Assert.IsTrue(await owner.InvalidateAsync(userId, definition.GridKey, includeSharedCache));
        await cache.Received(1).RemoveAsync(CacheEntryNames.GridPreference,
            CacheKeyBuilder.ForGlobal("Testing", "settings", "grid-preference",
                $"{userId:N}:{definition.GridKey}", $"v{definition.SchemaVersion}"),
            includeSharedCache ? CacheInvalidationScope.AllLayersSynchronous : CacheInvalidationScope.CurrentNodeOnly,
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Owner_port_rejects_unknown_grid_and_empty_user_without_cache_access()
    {
        var cache = Substitute.For<ICacheInvalidator>();
        await using var provider = CreateOwnerProvider(cache);
        var owner = provider.GetRequiredService<IGridPreferenceCacheInvalidator>();
        Assert.IsFalse(await owner.InvalidateAsync(Guid.NewGuid(), "unknown.grid", true));
        Assert.IsFalse(await owner.InvalidateAsync(Guid.Empty, "identity.users", true));
        Assert.IsFalse(await owner.InvalidateAsync(Guid.NewGuid(), "", true));
        Assert.IsEmpty(cache.ReceivedCalls());
    }

    [TestMethod]
    public async Task Owner_port_honors_cancellation_before_cache_access()
    {
        var cache = Substitute.For<ICacheInvalidator>();
        await using var provider = CreateOwnerProvider(cache);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
        {
            await provider.GetRequiredService<IGridPreferenceCacheInvalidator>()
                .InvalidateAsync(Guid.NewGuid(), "identity.users", true, cancellation.Token);
        });
        Assert.IsEmpty(cache.ReceivedCalls());
    }

    [TestMethod]
    public async Task Owner_port_propagates_the_original_cache_failure()
    {
        var cache = Substitute.For<ICacheInvalidator>();
        var original = new InvalidOperationException("fixture");
        cache.RemoveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CacheInvalidationScope>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromException(original));
        await using var provider = CreateOwnerProvider(cache);
        var observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            await provider.GetRequiredService<IGridPreferenceCacheInvalidator>()
                .InvalidateAsync(Guid.NewGuid(), "identity.users", true);
        });
        Assert.AreSame(original, observed);
    }

    private static ServiceProvider CreateOwnerProvider(ICacheInvalidator cache)
    {
        var services = new ServiceCollection();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Testing");
        services.AddSingleton(environment);
        services.AddSingleton(cache);
        new SettingsModule().AddServices(services, new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Oversized_grid_payload_is_neither_reused_nor_filled(bool seedLegacyValue)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetCaching(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:Entries:settings.grid-preference:OwnerModule"] = "settings",
            ["Cache:Entries:settings.grid-preference:ConsistencyClass"] = "S2",
            ["Cache:Entries:settings.grid-preference:L1Duration"] = "00:01:00",
            ["Cache:Entries:settings.grid-preference:L2Duration"] = "00:15:00",
            ["Cache:Entries:settings.grid-preference:MaxSerializedBytes"] = "1",
        }).Build(), "Testing");
        await using var provider = services.BuildServiceProvider();
        var query = Substitute.For<IQueryExecutor>();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Testing");
        var cache = provider.GetRequiredService<IFusionCache>();
        var policies = provider.GetRequiredService<ICachePolicyRegistry>();
        var userId = Guid.NewGuid();
        if (seedLegacyValue)
            await cache.SetAsync(CacheKeyBuilder.ForGlobal("Testing", "settings", "grid-preference",
                $"{userId:N}:identity.users", "v2"), new GridPreferenceResponse("identity.users", 2, [], 777),
                policies.CreateEntryOptions(CacheEntryNames.GridPreference));
        var service = new MyGridPreferenceService(query, Substitute.For<ICommandExecutor>(),
            new PassThroughTransaction(), cache, policies, environment, Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>(), provider.GetRequiredService<ICacheInvalidator>(),
            provider.GetRequiredService<IOptions<CacheOptions>>(), NullLogger<MyGridPreferenceService>.Instance);

        Assert.AreEqual(0, (await service.GetAsync(userId, "identity.users")).Value!.Version);
        Assert.AreEqual(0, (await service.GetAsync(userId, "identity.users")).Value!.Version);
        await query.Received(2).QuerySingleOrDefaultAsync<GridPreferenceRecord>(Arg.Any<SqlStatement>(),
            Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Committed_delete_remains_successful_when_cache_cleanup_fails()
    {
        var cache = Substitute.For<IFusionCache>();
        var invalidator = Substitute.For<ICacheInvalidator>();
        invalidator.RemoveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CacheInvalidationScope>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask>(_ => throw new InvalidOperationException("cache unavailable"));
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Testing");
        var service = new MyGridPreferenceService(Substitute.For<IQueryExecutor>(),
            Substitute.For<ICommandExecutor>(), new PassThroughTransaction(), cache,
            CachePolicyRegistry.Create(new CacheOptions()), environment,
            Substitute.For<IClock>(), Substitute.For<IIdGenerator>(), invalidator,
            Options.Create(new CacheOptions()), NullLogger<MyGridPreferenceService>.Instance);

        var result = await service.DeleteAsync(Guid.CreateVersion7(), "identity.users");

        Assert.IsTrue(result.IsSuccess, "数据库提交后的展示缓存故障不能把成功写入包装成失败。");
        await invalidator.Received(2).RemoveAsync(CacheEntryNames.GridPreference, Arg.Any<string>(),
            Arg.Any<CacheInvalidationScope>(), Arg.Is<CancellationToken>(token => token.CanBeCanceled));
    }

    // C0/N0 必须直读数据库；两次 Get 不得触发 HybridCache 工厂或条目选项构建。
    [TestMethod]
    [DataRow("C0")]
    [DataRow("N0")]
    public async Task Noncached_policy_reads_authority_without_creating_cache_options(string consistency)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetCaching(new ConfigurationBuilder().Build(), "Testing");
        await using var provider = services.BuildServiceProvider();
        var query = Substitute.For<IQueryExecutor>();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Testing");
        var policies = CachePolicyRegistry.Create(new CacheOptions
        {
            Entries = { [CacheEntryNames.GridPreference] = new CacheEntryDefinitionOptions
            { OwnerModule = "settings", ConsistencyClass = consistency } },
        });
        var service = new MyGridPreferenceService(query, Substitute.For<ICommandExecutor>(),
            new PassThroughTransaction(), provider.GetRequiredService<IFusionCache>(), policies,
            environment, Substitute.For<IClock>(), Substitute.For<IIdGenerator>(), Substitute.For<ICacheInvalidator>(),
            Options.Create(new CacheOptions()), NullLogger<MyGridPreferenceService>.Instance);

        Assert.IsTrue((await service.GetAsync(Guid.NewGuid(), "identity.users")).IsSuccess);
        Assert.IsTrue((await service.GetAsync(Guid.NewGuid(), "identity.users")).IsSuccess);
    }

    [TestMethod]
    public void Catalog_rejects_unknown_grid_keys()
    {
        Assert.IsTrue(GridPreferenceCatalog.TryGet("identity.users", out var definition));
        Assert.AreEqual(2, definition.SchemaVersion);
        Assert.IsFalse(GridPreferenceCatalog.TryGet("identity.remote-script", out _));
    }

    [TestMethod]
    public void Catalog_publishes_users_view_column_keys()
    {
        var definition = GridPreferenceCatalog.GetRequired("identity.users");
        CollectionAssert.AreEquivalent(
            new[]
            {
                "gender",
                "roles",
                "org",
                "position",
                "employeeNumber",
                "accountType",
                "sortOrder",
                "phone",
                "createdAt",
            },
            definition.ColumnKeys.ToArray());
    }

    [TestMethod]
    public void Validation_rejects_unknown_and_duplicate_column_keys()
    {
        var definition = GridPreferenceCatalog.GetRequired("identity.users");
        var unknown = GridPreferencePolicy.ValidateAndNormalize(
            definition,
            new UpdateGridPreferenceRequest(
                definition.SchemaVersion,
                [new GridColumnPreference("remoteScript", 0, 120, true, null)],
                0));
        var duplicate = GridPreferencePolicy.ValidateAndNormalize(
            definition,
            new UpdateGridPreferenceRequest(
                definition.SchemaVersion,
                [
                    new GridColumnPreference("phone", 0, 120, true, null),
                    new GridColumnPreference("phone", 1, 180, false, "left"),
                ],
                0));
        var missingColumns = GridPreferencePolicy.ValidateAndNormalize(
            definition,
            new UpdateGridPreferenceRequest(
                definition.SchemaVersion,
                null!,
                0));
        var nullColumn = GridPreferencePolicy.ValidateAndNormalize(
            definition,
            new UpdateGridPreferenceRequest(
                definition.SchemaVersion,
                [null!],
                0));

        Assert.IsFalse(unknown.IsSuccess);
        Assert.AreEqual(SettingsErrorCodes.GridColumnUnknown, unknown.Error!.Code);
        Assert.IsFalse(duplicate.IsSuccess);
        Assert.AreEqual(SettingsErrorCodes.GridColumnDuplicate, duplicate.Error!.Code);
        Assert.IsFalse(missingColumns.IsSuccess);
        Assert.AreEqual(
            SettingsErrorCodes.GridPreferenceInvalid,
            missingColumns.Error!.Code);
        Assert.IsFalse(nullColumn.IsSuccess);
        Assert.AreEqual(
            SettingsErrorCodes.GridPreferenceInvalid,
            nullColumn.Error!.Code);
    }

    [TestMethod]
    public void Validation_normalizes_order_and_preserves_supported_presentation()
    {
        var definition = GridPreferenceCatalog.GetRequired("identity.users");
        var result = GridPreferencePolicy.ValidateAndNormalize(
            definition,
            new UpdateGridPreferenceRequest(
                definition.SchemaVersion,
                [
                    new GridColumnPreference("createdAt", 2, 140, false, "right"),
                    new GridColumnPreference("phone", 0, 240, true, "left"),
                ],
                0));

        Assert.IsTrue(result.IsSuccess);
        var normalized = result.Value!;
        CollectionAssert.AreEqual(
            new[] { "phone", "createdAt" },
            normalized.Select(column => column.ColumnKey).ToArray());
        Assert.AreEqual(240, normalized[0].Width);
        Assert.AreEqual("left", normalized[0].Fixed);
        Assert.IsFalse(normalized[1].Visible);
    }

    [TestMethod]
    public void Schema_version_change_returns_safe_default()
    {
        var definition = GridPreferenceCatalog.GetRequired("identity.users");
        var restored = GridPreferencePolicy.Restore(
            definition,
            persistedSchemaVersion: definition.SchemaVersion - 1,
            persistedVersion: 7,
            [
                new GridColumnPreference("phone", 0, 320, false, "left"),
            ]);

        Assert.AreEqual("identity.users", restored.GridKey);
        Assert.AreEqual(definition.SchemaVersion, restored.SchemaVersion);
        Assert.AreEqual(0, restored.Version);
        Assert.IsEmpty(restored.Columns);

        var corrupted = GridPreferencePolicy.Restore(
            definition,
            definition.SchemaVersion,
            persistedVersion: 8,
            [null!]);
        Assert.AreEqual(0, corrupted.Version);
        Assert.IsEmpty(corrupted.Columns);
    }

    [TestMethod]
    public async Task Post_commit_cache_invalidation_ignores_request_cancellation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetCaching(new ConfigurationBuilder().Build(), "Testing");
        await using var provider = services.BuildServiceProvider();
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Testing");
        var policies = CachePolicyRegistry.Create(new CacheOptions());
        var service = new MyGridPreferenceService(
            Substitute.For<IQueryExecutor>(),
            command,
            new PassThroughTransaction(),
            provider.GetRequiredService<IFusionCache>(),
            policies,
            environment,
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>(), Substitute.For<ICacheInvalidator>(),
            Options.Create(new CacheOptions()), NullLogger<MyGridPreferenceService>.Instance);
        var userId = Guid.CreateVersion7();
        var primed = await service.GetAsync(
            userId,
            "identity.users",
            CancellationToken.None);
        Assert.IsTrue(primed.IsSuccess);
        using var source = new CancellationTokenSource();
        source.Cancel();

        var result = await service.DeleteAsync(
            userId,
            "identity.users",
            source.Token);

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public async Task Concurrent_first_write_unique_constraint_returns_version_conflict()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetCaching(new ConfigurationBuilder().Build(), "Testing");
        await using var provider = services.BuildServiceProvider();
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new DataCommandException(
                DataCommandFailureKind.UniqueConstraint,
                new InvalidOperationException("provider detail")));
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Testing");
        var policies = CachePolicyRegistry.Create(new CacheOptions());
        var service = new MyGridPreferenceService(
            Substitute.For<IQueryExecutor>(),
            command,
            new PassThroughTransaction(),
            provider.GetRequiredService<IFusionCache>(),
            policies,
            environment,
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>(), Substitute.For<ICacheInvalidator>(),
            Options.Create(new CacheOptions()), NullLogger<MyGridPreferenceService>.Instance);

        var result = await service.PutAsync(
            Guid.CreateVersion7(),
            "identity.users",
            new UpdateGridPreferenceRequest(2, [], 0));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            SettingsErrorCodes.GridPreferenceVersionConflict,
            result.Error!.Code);
    }

    private sealed class PassThroughTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            action(cancellationToken);
    }
}
