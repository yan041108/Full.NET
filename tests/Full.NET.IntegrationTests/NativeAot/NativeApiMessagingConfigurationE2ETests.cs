using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Messaging;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Messaging.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Testcontainers.MsSql;
using Testcontainers.MySql;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>真实 Linux ELF 验证 API 配置组合装配、认证和运维 JSON/查询；不外推完整控制面或 Worker CDC Native。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeApiMessagingConfigurationE2ETests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Native_api_binds_custom_retry_stages_and_serves_protected_operations(DatabaseProvider provider)
    {
        var root = Environment.GetEnvironmentVariable("FULLNET_MESSAGING_NATIVE_ROOT")
            ?? throw new InvalidOperationException("必须提供本轮真实 Native 发布目录；缺失不能跳过或报告通过。");
        var executable = Path.Combine(root, "publish", "Full.NET.Host.Api");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "manifest.json")));
        Assert.AreEqual(2, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
        // 机器码阶段只消费冻结的托管产物；其他模块后续源码变化必须记录，不能冒充它们的当前版本。
        foreach (var guard in new[] { "managedInputsVerified", "nativeInputsUnchanged", "managedArtifactsUnchanged", "headUnchanged" })
            Assert.IsTrue(manifest.RootElement.GetProperty(guard).GetBoolean(), guard);
        var digest = await DigestAsync(executable);
        Assert.AreEqual(manifest.RootElement.GetProperty("sha256").GetString(), digest);
        const string databaseName = "fullnet_native_messaging_config";
        IAsyncDisposable owner;
        DatabaseOptions database;
        string nativeConnection;
        if (provider == DatabaseProvider.SqlServer)
        {
            var server = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
                .WithPassword(SharedDatabaseFixture.MySqlRootPassword).WithEnvironment("MSSQL_MEMORY_LIMIT_MB", "2048")
                .WithCreateParameterModifier(p => { var limits = p.HostConfig ??= new(); limits.Memory = 3L << 30; limits.MemorySwap = limits.Memory; limits.NanoCPUs = 1_000_000_000; }).Build();
            owner = server;
            try
            {
                await server.StartAsync();
                var settings = new SqlConnectionStringBuilder(server.GetConnectionString());
                await using (var admin = new SqlConnection(settings.ConnectionString)) await admin.ExecuteAsync("CREATE DATABASE " + databaseName);
                settings.InitialCatalog = databaseName;
                database = new() { Provider = provider, ConnectionString = settings.ConnectionString };
                settings.DataSource = "host.docker.internal" + settings.DataSource[settings.DataSource.LastIndexOf(',')..];
                nativeConnection = settings.ConnectionString;
            }
            catch { await owner.DisposeAsync(); throw; }
        }
        else
        {
            var server = new MySqlBuilder("mysql:8.0").WithDatabase(databaseName).WithUsername("root")
                .WithPassword(SharedDatabaseFixture.MySqlRootPassword)
                .WithCreateParameterModifier(p => { var limits = p.HostConfig ??= new(); limits.Memory = 1536L << 20; limits.MemorySwap = limits.Memory; limits.NanoCPUs = 1_000_000_000; }).Build();
            owner = server;
            try
            {
                await server.StartAsync();
                database = new() { Provider = provider, ConnectionString = server.GetConnectionString(), MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16 };
                nativeConnection = new MySqlConnectionStringBuilder(database.ConnectionString) { Server = "host.docker.internal" }.ConnectionString;
            }
            catch { await owner.DisposeAsync(); throw; }
        }
        await using var cleanup = owner;
        await using var bootstrap = new FullNetApiFactory(provider, database.ConnectionString);
        await bootstrap.InitializeAsync(useSchemaTemplate: false);
        var definition = IdentityIntegrationEventTopicDefinitions.OrganizationUnitChanged;
        // 预置独享库的结构样本，确保 Native 总览真实物化新增列；不将其作为 Broker 切流证明。
        await using (var scope = EventDeliveryPilotTestSupport.CreateHostScope(bootstrap.Services))
        {
            var store = scope.ServiceProvider.GetRequiredService<IEventStreamOwnershipStore>();
            var transaction = scope.ServiceProvider.GetRequiredService<ICommandTransaction>();
            var now = DateTimeOffset.UtcNow;
            var binding = new CdcKafkaDeliveryBinding("native-fixture-cluster", "native-fixture-connector", "native-fixture-fingerprint",
                "native.fixture", ["native.fixture.group"], [], [new("native.fixture", "native-fixture-topic-id", [new(0, 0, 0)])]);
            await transaction.ExecuteAsync(async token =>
            {
                await store.UpsertAsync(new(definition.EventType, definition.SchemaVersion, definition.TopicCode,
                    EventDeliveryOwner.LegacyPolling, EventDeliveryOwner.LegacyPolling, Guid.CreateVersion7(), now,
                    null, null, "native materialization fixture", null, null, now, now)
                    { CdcKafkaBindingJson = binding.ToJson() }, token);
                return 0;
            }, CancellationToken.None);
        }
        await using var native = new ContainerBuilder(manifest.RootElement.GetProperty("imageId").GetString()!)
            .WithEntrypoint("/app/Full.NET.Host.Api").WithWorkingDirectory("/app")
            .WithBindMount(Path.Combine(root, "publish"), "/app", AccessMode.ReadOnly)
            .WithExtraHost("host.docker.internal", "host-gateway").WithPortBinding(8080, true)
            .WithEnvironment("DOTNET_ENVIRONMENT", "Development").WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
            .WithEnvironment("ASPNETCORE_URLS", "http://0.0.0.0:8080").WithEnvironment("ASPNETCORE_CONTENTROOT", "/app")
            .WithEnvironment("Database__Provider", provider.ToString()).WithEnvironment("Database__ConnectionString", nativeConnection)
            .WithEnvironment("Database__MySqlGuidStorageMode", "Binary16")
            .WithEnvironment("Identity__AllowDevelopmentEphemeralSigningKey", "true").WithEnvironment("Identity__RequireSecureCookies", "false")
            .WithEnvironment("Identity__SessionLoginPolicy", "AllowMultiple").WithEnvironment("Identity__EnableRemoteSuperAdministratorManagement", "true")
            .WithEnvironment("Tenancy__HostDomains__0", "localhost").WithEnvironment("Identity__AllowedOrigins__0", "http://localhost")
            .WithEnvironment("DataProtection__KeyRingPath", "/tmp/keys").WithEnvironment("Files__Local__RootPath", "/tmp/files")
            .WithEnvironment("Realtime__Enabled", "false")
            // API 仅启用配置校验和控制面；无常驻消费者，错误 Topic 在访问 Broker 前拒绝。
            .WithEnvironment("Messaging__Kafka__Enabled", "true").WithEnvironment("Messaging__Kafka__BootstrapServers", "127.0.0.1:1")
            .WithEnvironment("Messaging__Kafka__ClientId", "fullnet.native.messaging.config").WithEnvironment("Messaging__Kafka__SecurityProtocol", "Plaintext")
            .WithEnvironment("Messaging__Kafka__RetryStages__0", "1s").WithEnvironment("Messaging__Kafka__RetryStages__1", "2s").WithEnvironment("Messaging__Kafka__RetryStages__2", "3s")
            .WithEnvironment("Messaging__KafkaReplay__Enabled", "true")
            .WithCreateParameterModifier(p => { var limits = p.HostConfig ??= new(); limits.Memory = 1L << 30; limits.MemorySwap = limits.Memory; limits.NanoCPUs = 1_000_000_000; })
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8080).ForPath("/health/live"))).Build();
        await native.StartAsync();
        using var client = new HttpClient { BaseAddress = new($"http://127.0.0.1:{native.GetMappedPublicPort(8080)}"), Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Host = "localhost";
        using (var anonymous = await client.GetAsync("/api/v1/messaging/kafka/replay-operations")) Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", await FullNetApiFactoryLogin.LoginAsHostAdminAsync(client, default));
        using (var overview = await client.GetAsync("/api/v1/messaging/delivery-status/"))
        {
            Assert.AreEqual(HttpStatusCode.OK, overview.StatusCode, await overview.Content.ReadAsStringAsync());
            var body = await overview.Content.ReadFromJsonAsync<DeliveryStatusResponse>();
            var stream = body!.Streams.Single(item => item.EventType == definition.EventType && item.SchemaVersion == definition.SchemaVersion);
            Assert.AreEqual(EventDeliveryOwner.LegacyPolling, stream.DeliveryOwner,
                "新增拓扑列必须经真实 Native 所有权物化器读取，读取失败不能以 unknown 通过。");
        }
        using (var list = await client.GetAsync("/api/v1/messaging/kafka/replay-operations"))
        {
            Assert.AreEqual(HttpStatusCode.OK, list.StatusCode, await list.Content.ReadAsStringAsync());
            using var body = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            Assert.AreEqual(JsonValueKind.Array, body.RootElement.GetProperty("items").ValueKind);
        }
        using var invalid = await client.PostAsJsonAsync("/api/v1/messaging/kafka/replay-operations",
            new CreateKafkaReplayOperationRequest(Guid.CreateVersion7(), "native.unregistered-topic", null, null, "0", "0", [0], "fullnet.native.invalid", 1, "native configuration boundary"));
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode, await invalid.Content.ReadAsStringAsync());
        Assert.AreEqual(digest, await DigestAsync(executable));
    }

    private static async Task<string> DigestAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
    }
}
