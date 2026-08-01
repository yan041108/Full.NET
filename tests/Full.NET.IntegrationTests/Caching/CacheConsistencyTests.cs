using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using ZiggyCreatures.Caching.Fusion;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Caching;

[TestClass]
[DoNotParallelize]
public sealed class CacheConsistencyTests
{
    [TestMethod]
    public async Task SqlServer_provisioning_clears_negative_domain_cache_without_cache_outbox()
    {
        await VerifyProvisioningInvalidatesLocalNegativeCacheAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_provisioning_clears_negative_domain_cache_without_cache_outbox()
    {
        await VerifyProvisioningInvalidatesLocalNegativeCacheAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task SqlServer_provisioning_with_redis_backplane_repairs_secondary_without_cache_outbox()
    {
        await VerifyRedisBackplaneRepairsSecondaryNodeAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_provisioning_with_redis_backplane_repairs_secondary_without_cache_outbox()
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
        var cacheOutboxBeforeProvision = await CountTenantCacheOutboxAsync(
            databaseProvider,
            connectionString);

        await AssertTenantMissingAsync(primaryFactory, domain);
        await AssertTenantMissingAsync(secondaryFactory, domain);

        var provisioned = await ProvisionTenantAsync(
            primaryFactory,
            identifier,
            domain);

        Assert.AreEqual(
            cacheOutboxBeforeProvision,
            await CountTenantCacheOutboxAsync(databaseProvider, connectionString),
            "开通成功后不得再写入租户缓存专用 Outbox。");

        var localTenant = await AssertTenantFoundAsync(primaryFactory, domain);
        Assert.AreEqual(provisioned.Id, localTenant.Id);
        Assert.AreEqual(identifier, localTenant.Identifier);

        // 无 Redis Backplane 时，第二节点仍可保留负缓存，直到后续跨节点失效路径介入。
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
        var cacheOutboxBeforeProvision = await CountTenantCacheOutboxAsync(
            databaseProvider,
            connectionString);

        await AssertTenantMissingAsync(primaryFactory, domain);
        await AssertTenantMissingAsync(secondaryFactory, domain);
        primaryFactory.ClearBackplaneEvents();
        secondaryFactory.ClearBackplaneEvents();

        var provisioned = await ProvisionTenantAsync(primaryFactory, identifier, domain);
        Assert.AreEqual(
            cacheOutboxBeforeProvision,
            await CountTenantCacheOutboxAsync(databaseProvider, connectionString),
            "开通成功后不得再写入租户缓存专用 Outbox。");
        var localTenant = await AssertTenantFoundAsync(primaryFactory, domain);
        Assert.AreEqual(provisioned.Id, localTenant.Id);

        var secondaryDomainRemove = await WaitForBackplaneObservationAsync(
            secondaryFactory,
            "received",
            domainCacheKey);
        Assert.IsNotNull(
            secondaryDomainRemove,
            "Secondary 节点在提交后直接失效后仍未收到域名解析 key 的 backplane 删除通知。"
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

        var cacheOutboxBeforeUpdate = await CountTenantCacheOutboxAsync(
            databaseProvider,
            connectionString);
        var updated = await UpdateTenantAsync(
            primaryFactory,
            provisioned.Id,
            provisioned.Version,
            $"{identifier}-updated");
        Assert.AreEqual(
            cacheOutboxBeforeUpdate,
            await CountTenantCacheOutboxAsync(databaseProvider, connectionString),
            "更新成功后不得再写入租户缓存专用 Outbox。");
        var secondaryUpdatedTenant = await WaitForTenantAsync(
            secondaryFactory,
            domain,
            tenant => tenant.Name == updated.Name,
            "Secondary 节点未在提交后直接失效后观察到更新后的名称。");
        Assert.AreEqual(updated.Version, secondaryUpdatedTenant.Version);

        var cacheOutboxBeforeDisable = await CountTenantCacheOutboxAsync(
            databaseProvider,
            connectionString);
        var disabled = await DisableTenantAsync(primaryFactory, provisioned.Id);
        Assert.IsFalse(disabled.IsActive);
        Assert.AreEqual(
            cacheOutboxBeforeDisable,
            await CountTenantCacheOutboxAsync(databaseProvider, connectionString),
            "禁用成功后不得再写入租户缓存专用 Outbox。");
        await WaitForTenantMissingAsync(
            secondaryFactory,
            domain,
            "Secondary 节点未在提交后直接失效后观察到停用状态。");
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

    private static async Task<TenantSummary> UpdateTenantAsync(
        FullNetApiFactory factory,
        Guid tenantId,
        int version,
        string name)
    {
        using var client = factory.CreateClientForHost("localhost");
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/tenancy/tenants/{tenantId:D}")
        {
            Content = JsonContent.Create(new UpdateHostTenantRequest(name, version)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await LoginAsHostAdminAsync(client));
        using var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var tenant = await response.Content.ReadFromJsonAsync<TenantSummary>();
        Assert.IsNotNull(tenant);
        return tenant;
    }

    private static async Task<TenantSummary> DisableTenantAsync(
        FullNetApiFactory factory,
        Guid tenantId)
    {
        using var client = factory.CreateClientForHost("localhost");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenants/{tenantId:D}/disable")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await LoginAsHostAdminAsync(client));
        using var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var tenant = await response.Content.ReadFromJsonAsync<TenantSummary>();
        Assert.IsNotNull(tenant);
        return tenant;
    }

    private static async Task<string> LoginAsHostAdminAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new LoginRequest("admin", FullNetApiFactory.TestPassword)),
        };
        request.Headers.Add("Origin", "http://localhost");
        using var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.IsNotNull(token);
        return token.AccessToken;
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

    private static async Task<TenantSummary> WaitForTenantAsync(
        FullNetApiFactory factory,
        string domain,
        Func<TenantSummary, bool> predicate,
        string failureMessage)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < timeoutAt)
        {
            var tenant = await AssertTenantFoundAsync(factory, domain);
            if (predicate(tenant))
            {
                return tenant;
            }

            await Task.Delay(250);
        }

        Assert.Fail(failureMessage);
        return null!;
    }

    private static async Task WaitForTenantMissingAsync(
        FullNetApiFactory factory,
        string domain,
        string failureMessage)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < timeoutAt)
        {
            using var client = factory.CreateClientForHost(domain);
            using var response = await client.GetAsync("/api/v1/tenancy/current");
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return;
            }

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            await Task.Delay(250);
        }

        Assert.Fail(failureMessage);
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

    private static async Task<long> CountTenantCacheOutboxAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        await using var connection = CreateConnection(databaseProvider, connectionString);
        return await connection.QuerySingleAsync<long>(
            """
            SELECT COUNT(*)
            FROM fn_outbox_message
            WHERE MessageType IN (
                'fullnet.tenancy.tenant.provisioned',
                'fullnet.tenancy.tenant.changed')
            """);
    }

    private static Dictionary<string, string?> CreateRedisSettings(
        string redisConnectionString) => new()
        {
            [$"{CacheOptions.SectionName}:RedisConnectionString"] = redisConnectionString,
            ["Realtime:AllowSharedRedisInDevelopment"] = "true",
            ["ConnectionStrings:redis"] = redisConnectionString,
        };

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
}
