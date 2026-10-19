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
using Full.NET.Modules.Tenancy.Features.ManageHostTenants;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using ZiggyCreatures.Caching.Fusion;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Caching;

/// <summary>多实例租户准入：验证 C0 权威读取在 Redis 启用、缺失或故障时均立即反映已提交状态。</summary>
[TestClass]
[DoNotParallelize]
public sealed class CacheConsistencyTests
{
    /// <summary>租户解析已改为 C0 权威读；停用后须立即拒绝，不能依赖另一实例的 L1 负缓存。</summary>
    [TestMethod]
    public async Task SqlServer_disabled_tenant_is_denied_on_secondary_without_backplane()
    {
        await VerifyDisabledTenantAdmissionAsync(DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_disabled_tenant_is_denied_on_secondary_without_backplane()
    {
        await VerifyDisabledTenantAdmissionAsync(DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    private static async Task VerifyDisabledTenantAdmissionAsync(DatabaseProvider provider, string connectionString)
    {
        var identifier = $"authority-{Guid.NewGuid():N}";
        var domain = $"{identifier}.localhost";
        using var primary = new FullNetApiFactory(provider, connectionString);
        using var secondary = primary.CreateIsolatedFactory();
        await primary.InitializeAsync();
        await secondary.InitializeAsync();
        var tenant = await ProvisionTenantAsync(primary, identifier, domain);
        using var hostClient = secondary.CreateClientForHost("localhost");
        var token = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(hostClient,
            await LoginAsHostAdminAsync(hostClient), tenant.Id);
        using var domainClient = secondary.CreateClientForHost(domain);
        hostClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        domainClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        foreach (var client in new[] { hostClient, domainClient })
        {
            using var warm = await client.GetAsync("/api/v1/me");
            Assert.AreEqual(HttpStatusCode.OK, warm.StatusCode, await warm.Content.ReadAsStringAsync());
        }

        // 另一进程提交停用，没有 Redis/Backplane；避免重新登录使会话撤销掩盖租户准入缺陷。
        await using (var scope = primary.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
            context.SetHost();
            try
            {
                var result = await scope.ServiceProvider.GetRequiredService<HostTenantManagementService>()
                    .DisableAsync(tenant.Id);
                Assert.IsTrue(result.IsSuccess);
            }
            finally { context.Clear(); }
        }

        foreach (var client in new[] { hostClient, domainClient })
        {
            using var denied = await client.GetAsync("/api/v1/me");
            Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode, await denied.Content.ReadAsStringAsync());
            using var problem = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
            Assert.AreEqual("tenancy.context_mismatch", problem.RootElement.GetProperty("code").GetString());
        }
    }

    [TestMethod]
    public async Task SqlServer_provisioning_is_immediately_visible_on_both_nodes_without_cache_outbox()
    {
        await VerifyProvisioningIsImmediatelyVisibleAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_provisioning_is_immediately_visible_on_both_nodes_without_cache_outbox()
    {
        await VerifyProvisioningIsImmediatelyVisibleAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task SqlServer_provisioning_and_lifecycle_are_authoritative_with_redis_enabled()
    {
        await VerifyAuthoritativeLifecycleWithRedisAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_provisioning_and_lifecycle_are_authoritative_with_redis_enabled()
    {
        await VerifyAuthoritativeLifecycleWithRedisAsync(
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

    private static async Task VerifyProvisioningIsImmediatelyVisibleAsync(
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

        // 准入走权威查询；即使没有 Backplane，第二节点也不能保留旧负缓存。
        var remoteTenant = await AssertTenantFoundAsync(secondaryFactory, domain);
        Assert.AreEqual(provisioned.Id, remoteTenant.Id);
    }

    private static async Task VerifyAuthoritativeLifecycleWithRedisAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        var redisConnectionString =
            await SharedDatabaseFixture.GetRedisConnectionStringAsync();
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var identifier = $"redis-{suffix}";
        var domain = $"{identifier}.localhost";

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

        var provisioned = await ProvisionTenantAsync(primaryFactory, identifier, domain);
        Assert.AreEqual(
            cacheOutboxBeforeProvision,
            await CountTenantCacheOutboxAsync(databaseProvider, connectionString),
            "开通成功后不得再写入租户缓存专用 Outbox。");
        var localTenant = await AssertTenantFoundAsync(primaryFactory, domain);
        Assert.AreEqual(provisioned.Id, localTenant.Id);

        // 租户准入已经退出缓存；提交后的首次读取就必须正确，不能靠轮询等待广播或 TTL 收敛。
        var secondaryTenant = await AssertTenantFoundAsync(secondaryFactory, domain);
        Assert.AreEqual(provisioned.Id, secondaryTenant.Id);
        Assert.AreEqual(identifier, secondaryTenant.Identifier);

        var cacheOutboxBeforeUpdate = await CountTenantCacheOutboxAsync(
            databaseProvider,
            connectionString);
        var updated = await UpdateTenantAsync(
            primaryFactory,
            provisioned.Id,
            localTenant.Version,
            $"{identifier}-updated");
        Assert.AreEqual(
            cacheOutboxBeforeUpdate,
            await CountTenantCacheOutboxAsync(databaseProvider, connectionString),
            "更新成功后不得再写入租户缓存专用 Outbox。");
        var secondaryUpdatedTenant = await AssertTenantFoundAsync(secondaryFactory, domain);
        Assert.AreEqual(updated.Name, secondaryUpdatedTenant.Name);
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
        await AssertTenantMissingAsync(secondaryFactory, domain);
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

}
