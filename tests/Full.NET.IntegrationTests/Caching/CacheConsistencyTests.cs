extern alias workerhost;

using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Results;
using Full.NET.Caching.Fusion;
using Full.NET.Composition;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Serialization.MessagePack;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane;
using WorkerHost = workerhost::Full.NET.Host.Worker;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Caching;

[TestClass]
[DoNotParallelize]
public sealed class CacheConsistencyTests
{
    [TestMethod]
    public async Task SqlServer_provisioning_clears_negative_domain_cache_before_outbox_processing()
    {
        await VerifyProvisioningInvalidatesLocalNegativeCacheAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_provisioning_clears_negative_domain_cache_before_outbox_processing()
    {
        await VerifyProvisioningInvalidatesLocalNegativeCacheAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task SqlServer_provisioning_with_redis_backplane_eventually_repairs_secondary_negative_cache()
    {
        await VerifyRedisBackplaneRepairsSecondaryNodeAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_provisioning_with_redis_backplane_eventually_repairs_secondary_negative_cache()
    {
        await VerifyRedisBackplaneRepairsSecondaryNodeAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task SqlServer_provisioning_remains_immediately_visible_when_redis_is_unreachable()
    {
        await VerifyPrimaryNodeStaysCorrectWhenRedisIsUnavailableAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_provisioning_remains_immediately_visible_when_redis_is_unreachable()
    {
        await VerifyPrimaryNodeStaysCorrectWhenRedisIsUnavailableAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    private static async Task VerifyProvisioningInvalidatesLocalNegativeCacheAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var identifier = $"cache-{suffix}";
        var domain = $"{identifier}.localhost";

        using var primaryFactory = new FullNetApiFactory(databaseProvider, connectionString);
        using var secondaryFactory = primaryFactory.CreateIsolatedFactory();
        await primaryFactory.InitializeAsync();
        await secondaryFactory.InitializeAsync();
        var pendingOutboxBeforeProvision = await CountPendingOutboxAsync(
            databaseProvider,
            connectionString);
        var deadLetteredOutboxBeforeProvision = await CountDeadLetteredOutboxAsync(
            databaseProvider,
            connectionString);
        var retryScheduledOutboxBeforeProvision = await CountRetryScheduledOutboxAsync(
            databaseProvider,
            connectionString);

        await AssertTenantMissingAsync(primaryFactory, domain);
        await AssertTenantMissingAsync(secondaryFactory, domain);

        var provisioned = await ProvisionTenantAsync(
            primaryFactory,
            identifier,
            domain);

        Assert.AreEqual(
            pendingOutboxBeforeProvision + 1,
            await CountPendingOutboxAsync(databaseProvider, connectionString));

        var localTenant = await AssertTenantFoundAsync(primaryFactory, domain);
        Assert.AreEqual(provisioned.Id, localTenant.Id);
        Assert.AreEqual(identifier, localTenant.Identifier);

        // 第二节点仍未消费 Outbox，因此这里只锁定“跨节点修复尚未发生”的陈旧窗口。
        await AssertTenantMissingAsync(secondaryFactory, domain);
    }

    private static async Task VerifyRedisBackplaneRepairsSecondaryNodeAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        var redisConnectionString =
            await SharedDatabaseFixture.GetRedisConnectionStringAsync();
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var identifier = $"redis-{suffix}";
        var domain = $"{identifier}.localhost";
        var domainCacheKey = CacheKeyBuilder.TenantResolutionByDomain("Testing", domain);

        using var primaryFactory = new FullNetApiFactory(
            databaseProvider,
            connectionString,
            CreateRedisSettings(redisConnectionString));
        using var secondaryFactory = primaryFactory.CreateIsolatedFactory();
        await primaryFactory.InitializeAsync();
        await secondaryFactory.InitializeAsync();
        AssertDistinctCacheInstances(primaryFactory, secondaryFactory);
        var pendingOutboxBeforeProvision = await CountPendingOutboxAsync(
            databaseProvider,
            connectionString);
        var deadLetteredOutboxBeforeProvision = await CountDeadLetteredOutboxAsync(
            databaseProvider,
            connectionString);
        var retryScheduledOutboxBeforeProvision = await CountRetryScheduledOutboxAsync(
            databaseProvider,
            connectionString);
        var processedOutboxBeforeProvision = await CountProcessedOutboxAsync(
            databaseProvider,
            connectionString);

        await AssertTenantMissingAsync(primaryFactory, domain);
        await AssertTenantMissingAsync(secondaryFactory, domain);
        primaryFactory.ClearBackplaneEvents();
        secondaryFactory.ClearBackplaneEvents();

        var provisioned = await ProvisionTenantAsync(primaryFactory, identifier, domain);
        var provisionedOutboxId = await GetLatestTenantProvisionedOutboxIdAsync(
            databaseProvider,
            connectionString);
        Assert.AreEqual(
            pendingOutboxBeforeProvision + 1,
            await CountPendingOutboxAsync(databaseProvider, connectionString));
        Assert.AreEqual(
            deadLetteredOutboxBeforeProvision,
            await CountDeadLetteredOutboxAsync(databaseProvider, connectionString));
        Assert.AreEqual(
            retryScheduledOutboxBeforeProvision,
            await CountRetryScheduledOutboxAsync(databaseProvider, connectionString));
        var localTenant = await AssertTenantFoundAsync(primaryFactory, domain);
        Assert.AreEqual(provisioned.Id, localTenant.Id);

        // 共享 L2/标签版本可能让第二节点在 Outbox 消费前提前看见新租户；
        // 本场景只要求 Outbox 最终仍发出可观测的跨节点精确失效通知。
        await using (var failingWorkerServices = BuildWorkerServices(
                         CreateWorkerConfiguration(
                             databaseProvider,
                             connectionString,
                             redisConnectionString)))
        {
            var failingWorkerCache =
                failingWorkerServices.GetRequiredService<IFusionCache>();
            failingWorkerCache.RemoveBackplane();
            failingWorkerCache.SetupBackplane(new ThrowingBackplane());
            Assert.IsTrue(
                failingWorkerCache.HasBackplane,
                "失败路径必须保留 Backplane，才能验证广播异常不会被当作成功确认。");
            await CreateProcessor(failingWorkerServices)
                .ProcessOnceAsync(CancellationToken.None);
        }

        var failedAttempt = await GetOutboxStateAsync(
            databaseProvider,
            connectionString,
            provisionedOutboxId);
        Assert.IsNull(
            failedAttempt.ProcessedAtUtc,
            "Backplane 不可达时不得把当前租户事件标记为已处理。");
        Assert.IsNull(
            failedAttempt.DeadLetteredAtUtc,
            "首次 Backplane 发布失败应进入重试，而不是直接进入死信。");
        Assert.IsNotNull(
            failedAttempt.NextAttemptAtUtc,
            "Backplane 发布失败后当前租户事件必须释放租约并安排重试。");
        Assert.IsGreaterThanOrEqualTo(
            retryScheduledOutboxBeforeProvision + pendingOutboxBeforeProvision + 1,
            await CountRetryScheduledOutboxAsync(databaseProvider, connectionString),
            "Backplane 发布失败后 Outbox 必须释放租约并安排重试。");
        await MakeOutboxRetriesDueAsync(databaseProvider, connectionString);

        await using var workerServices = BuildWorkerServices(
            CreateWorkerConfiguration(
                databaseProvider,
                connectionString,
                redisConnectionString));
        var workerCache = workerServices.GetRequiredService<IFusionCache>();
        Assert.IsTrue(
            workerCache.HasBackplane,
            "Worker 缓存实例必须连接 Redis Backplane，才能完成跨节点失效广播。");
        var workerBackplaneEvents = new ConcurrentQueue<string>();
        workerCache.Events.Backplane.MessagePublished += (_, args) =>
            workerBackplaneEvents.Enqueue(
                $"{args.Message.Action}:{args.Message.CacheKey ?? "<null>"}");
        var processor = CreateProcessor(workerServices);
        await processor.ProcessOnceAsync(CancellationToken.None);
        var successfulAttempt = await GetOutboxStateAsync(
            databaseProvider,
            connectionString,
            provisionedOutboxId);
        Assert.IsNotNull(
            successfulAttempt.ProcessedAtUtc,
            "Backplane 恢复后当前租户事件必须成功确认。");
        Assert.IsNull(successfulAttempt.DeadLetteredAtUtc);
        Assert.AreEqual(
            0L,
            await CountPendingOutboxAsync(databaseProvider, connectionString),
            "Worker 处理后仍残留可领取的 Outbox 消息，说明缓存修复链路并未真正完成。");
        Assert.AreEqual(
            deadLetteredOutboxBeforeProvision,
            await CountDeadLetteredOutboxAsync(databaseProvider, connectionString),
            "Worker 不应把租户创建事件送入死信；若这里增长，需优先排查处理器匹配或反序列化失败。");
        Assert.IsLessThanOrEqualTo(
            retryScheduledOutboxBeforeProvision,
            await CountRetryScheduledOutboxAsync(databaseProvider, connectionString),
            "Worker 不应把租户创建事件释放回重试队列；若这里增长，需优先排查处理器运行期异常。");
        Assert.IsGreaterThanOrEqualTo(
            processedOutboxBeforeProvision + pendingOutboxBeforeProvision + 1,
            await CountProcessedOutboxAsync(databaseProvider, connectionString),
            "Worker 未把租户创建事件标记为已处理，说明缓存修复链路尚未走到成功确认阶段。");
        var secondaryDomainRemove = await WaitForBackplaneObservationAsync(
            secondaryFactory,
            "received",
            domainCacheKey);
        Assert.IsNotNull(
            secondaryDomainRemove,
            "Secondary 节点在 Worker 处理后仍未收到域名解析 key 的 backplane 删除通知。"
            + Environment.NewLine
            + $"Worker events: {string.Join(" | ", workerBackplaneEvents)}"
            + Environment.NewLine
            + $"Primary events: {DescribeBackplaneEvents(primaryFactory)}"
            + Environment.NewLine
            + $"Secondary events: {DescribeBackplaneEvents(secondaryFactory)}");

        var secondaryTenant = await WaitForTenantFoundAsync(
            secondaryFactory,
            domain,
            () =>
                $"Primary events: {DescribeBackplaneEvents(primaryFactory)}"
                + Environment.NewLine
                + $"Secondary events: {DescribeBackplaneEvents(secondaryFactory)}");
        Assert.AreEqual(provisioned.Id, secondaryTenant.Id);
        Assert.AreEqual(identifier, secondaryTenant.Identifier);
    }

    private static async Task VerifyPrimaryNodeStaysCorrectWhenRedisIsUnavailableAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var identifier = $"redis-down-{suffix}";
        var domain = $"{identifier}.localhost";

        using var primaryFactory = new FullNetApiFactory(
            databaseProvider,
            connectionString,
            CreateRedisSettings(
                "127.0.0.1:1,abortConnect=false,connectTimeout=500,syncTimeout=500"));
        await primaryFactory.InitializeAsync();

        await AssertTenantMissingAsync(primaryFactory, domain);
        var provisioned = await ProvisionTenantAsync(primaryFactory, identifier, domain);
        var localTenant = await AssertTenantFoundAsync(primaryFactory, domain);

        Assert.AreEqual(provisioned.Id, localTenant.Id);
        Assert.AreEqual(identifier, localTenant.Identifier);
    }

    private static async Task<TenantSummary> ProvisionTenantAsync(
        FullNetApiFactory factory,
        string identifier,
        string domain)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            var result = await scope.ServiceProvider
                .GetRequiredService<ITenantProvisioningService>()
                .ProvisionAsync(
                    new ProvisionTenantRequest(
                        identifier,
                        "Cache Consistency Tenant",
                        domain));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(result.Value);
            return result.Value;
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private static async Task AssertTenantMissingAsync(
        FullNetApiFactory factory,
        string domain)
    {
        using var client = factory.CreateClientForHost(domain);
        using var response = await client.GetAsync("/api/v1/tenancy/current");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(
            "tenancy.host_not_found",
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task<TenantSummary> AssertTenantFoundAsync(
        FullNetApiFactory factory,
        string domain)
    {
        using var client = factory.CreateClientForHost(domain);
        using var response = await client.GetAsync("/api/v1/tenancy/current");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var tenant = JsonSerializer.Deserialize<TenantSummary>(
            await response.Content.ReadAsStringAsync(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.IsNotNull(tenant);
        return tenant;
    }

    private static async Task<TenantSummary> WaitForTenantFoundAsync(
        FullNetApiFactory factory,
        string domain,
        Func<string>? failureDetailsFactory = null)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < timeoutAt)
        {
            using var client = factory.CreateClientForHost(domain);
            using var response = await client.GetAsync("/api/v1/tenancy/current");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var tenant = JsonSerializer.Deserialize<TenantSummary>(
                    await response.Content.ReadAsStringAsync(),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                Assert.IsNotNull(tenant);
                return tenant;
            }

            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
            await Task.Delay(250);
        }

        var details = failureDetailsFactory?.Invoke();
        Assert.Fail(
            $"Tenant '{domain}' was not visible on the secondary node within the expected window."
            + (string.IsNullOrWhiteSpace(details)
                ? string.Empty
                : Environment.NewLine + details));
        return null!;
    }

    private static async Task<BackplaneEventObservation?> WaitForBackplaneObservationAsync(
        FullNetApiFactory factory,
        string direction,
        string cacheKey)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < timeoutAt)
        {
            var observation = factory.GetBackplaneEventsSnapshot()
                .LastOrDefault(item =>
                    string.Equals(item.Direction, direction, StringComparison.Ordinal)
                    && string.Equals(item.CacheKey, cacheKey, StringComparison.Ordinal));
            if (observation is not null)
            {
                return observation;
            }

            await Task.Delay(100);
        }

        return null;
    }

    private static async Task<long> CountPendingOutboxAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        await using var connection = CreateConnection(databaseProvider, connectionString);
        return await connection.QuerySingleAsync<long>(
            """
            SELECT COUNT(*)
            FROM fn_outbox_message
            WHERE ProcessedAtUtc IS NULL
              AND DeadLetteredAtUtc IS NULL
              AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= CURRENT_TIMESTAMP)
            """);
    }

    private static async Task<long> CountProcessedOutboxAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        await using var connection = CreateConnection(databaseProvider, connectionString);
        return await connection.QuerySingleAsync<long>(
            """
            SELECT COUNT(*)
            FROM fn_outbox_message
            WHERE ProcessedAtUtc IS NOT NULL
              AND DeadLetteredAtUtc IS NULL
            """);
    }

    private static async Task<Guid> GetLatestTenantProvisionedOutboxIdAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        await using var connection = CreateConnection(databaseProvider, connectionString);
        var sql = databaseProvider == DatabaseProvider.SqlServer
            ? """
              SELECT TOP (1) Id
              FROM fn_outbox_message
              WHERE MessageType = 'fullnet.tenancy.tenant.provisioned'
              ORDER BY OccurredAtUtc DESC, Id DESC
              """
            : """
              SELECT Id
              FROM fn_outbox_message
              WHERE MessageType = 'fullnet.tenancy.tenant.provisioned'
              ORDER BY OccurredAtUtc DESC, Id DESC
              LIMIT 1
              """;
        return await connection.QuerySingleAsync<Guid>(sql);
    }

    private static async Task<OutboxState> GetOutboxStateAsync(
        DatabaseProvider databaseProvider,
        string connectionString,
        Guid messageId)
    {
        await using var connection = CreateConnection(databaseProvider, connectionString);
        return await connection.QuerySingleAsync<OutboxState>(
            """
            SELECT ProcessedAtUtc, DeadLetteredAtUtc, NextAttemptAtUtc
            FROM fn_outbox_message
            WHERE Id = @MessageId
            """,
            new { MessageId = messageId });
    }

    private static async Task<long> CountDeadLetteredOutboxAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        await using var connection = CreateConnection(databaseProvider, connectionString);
        return await connection.QuerySingleAsync<long>(
            """
            SELECT COUNT(*)
            FROM fn_outbox_message
            WHERE ProcessedAtUtc IS NULL
              AND DeadLetteredAtUtc IS NOT NULL
            """);
    }

    private static async Task<long> CountRetryScheduledOutboxAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        await using var connection = CreateConnection(databaseProvider, connectionString);
        return await connection.QuerySingleAsync<long>(
            """
            SELECT COUNT(*)
            FROM fn_outbox_message
            WHERE ProcessedAtUtc IS NULL
              AND DeadLetteredAtUtc IS NULL
              AND NextAttemptAtUtc IS NOT NULL
            """);
    }

    private static async Task MakeOutboxRetriesDueAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        await using var connection = CreateConnection(databaseProvider, connectionString);
        await connection.ExecuteAsync(
            """
            UPDATE fn_outbox_message
            SET NextAttemptAtUtc = CURRENT_TIMESTAMP
            WHERE ProcessedAtUtc IS NULL
              AND DeadLetteredAtUtc IS NULL
              AND NextAttemptAtUtc IS NOT NULL
            """);
    }

    private static Dictionary<string, string?> CreateRedisSettings(
        string redisConnectionString) => new()
        {
            [$"{CacheOptions.SectionName}:RedisConnectionString"] = redisConnectionString,
            ["ConnectionStrings:redis"] = redisConnectionString,
        };

    private sealed record OutboxState(
        DateTimeOffset? ProcessedAtUtc,
        DateTimeOffset? DeadLetteredAtUtc,
        DateTimeOffset? NextAttemptAtUtc);

    private sealed class ThrowingBackplane : IFusionCacheBackplane
    {
        public void Subscribe(BackplaneSubscriptionOptions options)
        {
        }

        public ValueTask SubscribeAsync(BackplaneSubscriptionOptions options) =>
            ValueTask.CompletedTask;

        public void Unsubscribe()
        {
        }

        public ValueTask UnsubscribeAsync() => ValueTask.CompletedTask;

        public void Publish(
            BackplaneMessage message,
            FusionCacheEntryOptions options,
            CancellationToken token = default) =>
            throw new InvalidOperationException("Simulated backplane publication failure.");

        public ValueTask PublishAsync(
            BackplaneMessage message,
            FusionCacheEntryOptions options,
            CancellationToken token = default) =>
            ValueTask.FromException(
                new InvalidOperationException("Simulated backplane publication failure."));

        public void Dispose()
        {
        }
    }

    private static IConfiguration CreateWorkerConfiguration(
        DatabaseProvider databaseProvider,
        string connectionString,
        string redisConnectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{DatabaseOptions.SectionName}:Provider"] = databaseProvider.ToString(),
                [$"{DatabaseOptions.SectionName}:ConnectionString"] = connectionString,
                [$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] =
                    MySqlGuidStorageMode.Binary16.ToString(),
                [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] = "30",
                [$"{CacheOptions.SectionName}:RedisConnectionString"] = redisConnectionString,
                ["ConnectionStrings:redis"] = redisConnectionString,
            })
            .Build();

    private static ServiceProvider BuildWorkerServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<IHostEnvironment, TestHostEnvironment>();
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenant>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.AddSingleton<IApiResultMapper, NonHttpApiResultMapper>();
        services.AddSingleton<
            ITenantOrganizationUnitDirectory,
            EmptyTenantOrganizationUnitDirectory>();
        services.AddFullNetDapper(configuration, "Testing");
        services.AddFullNetMessagePack();
        services.AddFullNetCaching(configuration, "Testing");
        services.AddFullNetApplicationModules(
            configuration,
            FullNetHostProfile.Worker);
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private static WorkerHost.OutboxProcessor CreateProcessor(ServiceProvider services) => new(
        services.GetRequiredService<IServiceScopeFactory>(),
        services.GetRequiredService<IClock>(),
        Options.Create(new WorkerHost.OutboxWorkerOptions
        {
            BatchSize = 20,
            LeaseSeconds = 30,
            PollMilliseconds = 1000,
            MaxAttempts = 3,
        }),
        NullLogger<WorkerHost.OutboxProcessor>.Instance);

    private static DbConnection CreateConnection(
        DatabaseProvider databaseProvider,
        string connectionString) => databaseProvider switch
        {
            DatabaseProvider.SqlServer => new SqlConnection(connectionString),
            DatabaseProvider.MySql => new MySqlConnection(
                MySqlConnectionStringPolicy.Create(
                    connectionString,
                    MySqlGuidStorageMode.Binary16,
                    allowUserVariables: false)),
            _ => throw new ArgumentOutOfRangeException(nameof(databaseProvider)),
        };

    private static void AssertDistinctCacheInstances(
        FullNetApiFactory primaryFactory,
        FullNetApiFactory secondaryFactory)
    {
        var primaryCache = primaryFactory.Services.GetRequiredService<IFusionCache>();
        var secondaryCache = secondaryFactory.Services.GetRequiredService<IFusionCache>();

        Assert.AreNotEqual(
            primaryCache.InstanceId,
            secondaryCache.InstanceId,
            "Redis/Backplane 测试必须使用两个不同的 FusionCache 实例；共享同一个 InstanceId 会让节点间失效广播失真。");
    }

    private static string DescribeBackplaneEvents(FullNetApiFactory factory)
    {
        var events = factory.GetBackplaneEventsSnapshot();
        if (events.Count == 0)
        {
            return $"none (instance {factory.CacheInstanceId})";
        }

        return string.Join(
            " | ",
            events.Select(item =>
                $"{item.Direction}:{item.Action}:{item.CacheKey ?? "<null>"}:{item.SourceId}"));
    }

    private sealed class EmptyTenantOrganizationUnitDirectory
        : ITenantOrganizationUnitDirectory
    {
        public Task<TenantOrganizationUnitDirectoryEntry?> FindActiveUnitAsync(
            Guid tenantId,
            Guid unitId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TenantOrganizationUnitDirectoryEntry?>(null);
    }

    private sealed class NonHttpApiResultMapper : IApiResultMapper
    {
        public IResult Map<T>(Result<T> result, HttpContext httpContext) =>
            throw new NotSupportedException("The non-HTTP integration fixture does not map API results.");

        public IResult MapException(Exception exception, HttpContext httpContext) =>
            throw new NotSupportedException("The non-HTTP integration fixture does not map API exceptions.");
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";

        public string ApplicationName { get; set; } = "Full.NET.IntegrationTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
