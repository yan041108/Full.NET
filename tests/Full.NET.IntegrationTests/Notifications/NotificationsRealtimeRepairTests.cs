extern alias workerhost;

using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Dapper;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Caching.Fusion;
using Full.NET.Composition;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Data.MySql;
using Full.NET.Hosting.Api;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Realtime;
using Full.NET.Realtime.SignalR;
using Full.NET.Serialization.MessagePack;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;
using WorkerHost = workerhost::Full.NET.Host.Worker;

namespace Full.NET.IntegrationTests.Notifications;

[TestClass]
[DoNotParallelize]
public sealed class NotificationsRealtimeRepairTests
{
    [TestMethod]
    public async Task SqlServer_worker_repairs_inbox_realtime_delivery_through_redis()
    {
        await VerifyInboxRealtimeRepairAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_worker_repairs_inbox_realtime_delivery_through_redis()
    {
        await VerifyInboxRealtimeRepairAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    private static async Task VerifyInboxRealtimeRepairAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        var redisConnectionString =
            await SharedDatabaseFixture.GetRedisConnectionStringAsync();
        var subscriberSettings = new Dictionary<string, string?>
        {
            [$"{RealtimeOptions.SectionName}:RedisBackplaneConnectionString"] =
                redisConnectionString,
            ["ConnectionStrings:redis"] = redisConnectionString,
        };
        var writerSettings = new Dictionary<string, string?>
        {
            [$"{RealtimeOptions.SectionName}:Enabled"] = "false",
        };
        using var subscriberFactory = new FullNetApiFactory(
            databaseProvider,
            connectionString,
            subscriberSettings);
        using var writerFactory = new FullNetApiFactory(
            databaseProvider,
            connectionString,
            writerSettings);
        await subscriberFactory.InitializeAsync();
        await writerFactory.InitializeAsync();

        var recipient = await subscriberFactory.CreateHostIdentityAsync(
            $"notification-repair-{Guid.NewGuid():N}",
            []);
        var receivedMessages = Channel.CreateUnbounded<RealtimeMessage>();
        await using var connection = CreateConnection(
            subscriberFactory,
            recipient.AccessToken,
            receivedMessages.Writer);
        await connection.StartAsync();

        var title = $"Worker 补偿-{Guid.NewGuid():N}"[..24];
        var created = await SendInboxMessageAsync(
            writerFactory,
            recipient.UserId,
            title);
        Assert.IsNull(
            await WaitForMessageAsync(
                receivedMessages.Reader,
                RealtimeMessageCodes.InboxMessageReceived,
                TimeSpan.FromSeconds(1)),
            "写入节点关闭 Realtime 后，不得由提交后的直接路径伪造 Worker 补偿成功。");

        var outboxId = await GetOutboxIdAsync(
            databaseProvider,
            connectionString);
        using var workerHost = BuildWorkerHost(
            CreateWorkerConfiguration(
                databaseProvider,
                connectionString,
                redisConnectionString));
        await workerHost.StartAsync();
        try
        {
            var processedCount = await CreateProcessor(workerHost.Services)
                .ProcessOnceAsync(CancellationToken.None);

            Assert.IsGreaterThanOrEqualTo(
                1,
                processedCount,
                "Worker 必须实际领取并处理至少一条 Outbox 消息。");
            var received = await WaitForMessageAsync(
                receivedMessages.Reader,
                RealtimeMessageCodes.InboxMessageReceived,
                TimeSpan.FromSeconds(10));
            Assert.IsNotNull(
                received,
                "独立 Worker 未经 Redis Backplane 把站内信修复消息送达订阅 API 节点。");
            Assert.AreEqual(created.Id, ReadGuid(received.Data, "messageId"));
            Assert.AreEqual(title, ReadString(received.Data, "title"));

            var unread = await WaitForMessageAsync(
                receivedMessages.Reader,
                RealtimeMessageCodes.InboxUnreadCountChanged,
                TimeSpan.FromSeconds(10));
            Assert.IsNotNull(unread, "Worker 修复送达后必须同步发布当前未读数。");
            Assert.IsGreaterThanOrEqualTo(
                1L,
                ReadInt64(unread.Data, "unreadCount"));
            Assert.IsTrue(
                await IsOutboxProcessedAsync(
                    databaseProvider,
                    connectionString,
                    outboxId),
                "只有 Redis 发布完成后，Notifications Outbox 才能进入成功终态。");
        }
        finally
        {
            await workerHost.StopAsync();
        }
    }

    private static HubConnection CreateConnection(
        FullNetApiFactory factory,
        string accessToken,
        ChannelWriter<RealtimeMessage> writer)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(
                "http://localhost/hubs/notifications",
                options =>
                {
                    options.AccessTokenProvider = () =>
                        Task.FromResult<string?>(accessToken);
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ =>
                        factory.Server.CreateHandler();
                })
            .Build();
        connection.On<RealtimeMessage>(
            "ReceiveMessageAsync",
            message => writer.TryWrite(message));
        return connection;
    }

    private static async Task<InboxMessageResponse> SendInboxMessageAsync(
        FullNetApiFactory factory,
        Guid recipientUserId,
        string title)
    {
        using var client = factory.CreateClientForHost("localhost");
        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new LoginRequest("admin", FullNetApiFactory.TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.IsNotNull(token);

        using var sendRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/notifications/host-inbox-messages")
        {
            Content = JsonContent.Create(
                new SendHostInboxMessageRequest(
                    recipientUserId,
                    title,
                    "Worker Outbox 实时补偿集成测试")),
        };
        sendRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token.AccessToken);
        using var sendResponse = await client.SendAsync(sendRequest);
        Assert.AreEqual(HttpStatusCode.Created, sendResponse.StatusCode);
        var created =
            await sendResponse.Content.ReadFromJsonAsync<InboxMessageResponse>();
        Assert.IsNotNull(created);
        return created;
    }

    private static async Task<RealtimeMessage?> WaitForMessageAsync(
        ChannelReader<RealtimeMessage> reader,
        string code,
        TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        try
        {
            while (await reader.WaitToReadAsync(timeoutSource.Token))
            {
                while (reader.TryRead(out var message))
                {
                    if (string.Equals(message.Code, code, StringComparison.Ordinal))
                    {
                        return message;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
        }

        return null;
    }

    private static IConfiguration CreateWorkerConfiguration(
        DatabaseProvider databaseProvider,
        string connectionString,
        string redisConnectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{DatabaseOptions.SectionName}:Provider"] =
                    databaseProvider.ToString(),
                [$"{DatabaseOptions.SectionName}:ConnectionString"] =
                    connectionString,
                [$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] =
                    MySqlGuidStorageMode.Binary16.ToString(),
                [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] = "30",
                [$"{CacheOptions.SectionName}:RedisConnectionString"] =
                    redisConnectionString,
                [$"{RealtimeOptions.SectionName}:RedisBackplaneConnectionString"] =
                    redisConnectionString,
                [$"{RealtimeOptions.SectionName}:AllowSharedRedisInDevelopment"] =
                    "true",
                ["ConnectionStrings:redis"] = redisConnectionString,
                ["Files:Local:RootPath"] = Path.Combine(
                    Path.GetTempPath(),
                    "fullnet-files-integration",
                    $"notifications-worker-{Guid.NewGuid():N}"),
            })
            .Build();

    private static IHost BuildWorkerHost(
        IConfiguration configuration)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "Full.NET.IntegrationTests.Notifications.Worker",
            EnvironmentName = "Testing",
        });
        builder.Configuration.AddConfiguration(configuration);
        var services = builder.Services;
        services.AddLogging();
        services.AddRouting();
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenant>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.AddSingleton<IApiResultMapper, NonHttpApiResultMapper>();
        services.AddSingleton<
            ITenantOrganizationUnitDirectory,
            EmptyTenantOrganizationUnitDirectory>();
        services.AddSingleton<
            IIdentityOrganizationUnitDirectory,
            EmptyIdentityOrganizationUnitDirectory>();
        services.AddFullNetDapper(configuration, "Testing");
        services.AddFullNetMessagePack();
        services.AddFullNetCaching(configuration, "Testing");
        services.AddFullNetRealtimePublisher(configuration, "Testing");
        services.AddFullNetApplicationModules(
            builder.Configuration,
            FullNetHostProfile.Worker);
        // 本测试只手动驱动 Outbox；移除其它模块后台循环，避免竞争同一数据库或制造无关日志。
        services.RemoveAll<IHostedService>();
        return builder.Build();
    }

    private static WorkerHost.OutboxProcessor CreateProcessor(
        IServiceProvider services) => new(
        services.GetRequiredService<IServiceScopeFactory>(),
        services.GetRequiredService<IClock>(),
        Options.Create(new WorkerHost.OutboxWorkerOptions
        {
            BatchSize = 100,
            LeaseSeconds = 30,
            PollMilliseconds = 1000,
            MaxAttempts = 3,
        }),
        NullLogger<WorkerHost.OutboxProcessor>.Instance);

    private static async Task<Guid> GetOutboxIdAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        await using var connection =
            CreateDatabaseConnection(databaseProvider, connectionString);
        return await connection.QuerySingleAsync<Guid>(
            """
            SELECT Id
            FROM fn_outbox_message
            WHERE MessageType = @MessageType
              AND ProcessedAtUtc IS NULL
              AND DeadLetteredAtUtc IS NULL
            ORDER BY OccurredAtUtc DESC, Id DESC
            """,
            new
            {
                MessageType =
                    NotificationRealtimeEventTypes.InboxMessageReceived,
            });
    }

    private static async Task<bool> IsOutboxProcessedAsync(
        DatabaseProvider databaseProvider,
        string connectionString,
        Guid outboxId)
    {
        await using var connection =
            CreateDatabaseConnection(databaseProvider, connectionString);
        return await connection.QuerySingleAsync<bool>(
            """
            SELECT CASE WHEN ProcessedAtUtc IS NULL THEN 0 ELSE 1 END
            FROM fn_outbox_message
            WHERE Id = @OutboxId
            """,
            new { OutboxId = outboxId });
    }

    private static DbConnection CreateDatabaseConnection(
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

    private static Guid ReadGuid(
        IReadOnlyDictionary<string, object?>? data,
        string key)
    {
        Assert.IsNotNull(data);
        Assert.IsTrue(data.TryGetValue(key, out var value));
        return value switch
        {
            Guid guid => guid,
            string text => Guid.Parse(text),
            JsonElement { ValueKind: JsonValueKind.String } element =>
                element.GetGuid(),
            _ => throw new AssertFailedException(
                $"Realtime data '{key}' 不是 Guid：{value?.GetType().FullName ?? "<null>"}。"),
        };
    }

    private static string ReadString(
        IReadOnlyDictionary<string, object?>? data,
        string key)
    {
        Assert.IsNotNull(data);
        Assert.IsTrue(data.TryGetValue(key, out var value));
        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element =>
                element.GetString()
                ?? throw new AssertFailedException(
                    $"Realtime data '{key}' 的字符串值为空。"),
            _ => throw new AssertFailedException(
                $"Realtime data '{key}' 不是 string：{value?.GetType().FullName ?? "<null>"}。"),
        };
    }

    private static long ReadInt64(
        IReadOnlyDictionary<string, object?>? data,
        string key)
    {
        Assert.IsNotNull(data);
        Assert.IsTrue(data.TryGetValue(key, out var value));
        return value switch
        {
            JsonElement { ValueKind: JsonValueKind.Number } element =>
                element.GetInt64(),
            _ => Convert.ToInt64(
                value,
                System.Globalization.CultureInfo.InvariantCulture),
        };
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

    private sealed class EmptyIdentityOrganizationUnitDirectory
        : IIdentityOrganizationUnitDirectory
    {
        public Task<IdentityOrganizationUnitDirectoryEntry?> FindActiveUnitAsync(
            Guid tenantId,
            Guid unitId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IdentityOrganizationUnitDirectoryEntry?>(null);
    }

    private sealed class NonHttpApiResultMapper : IApiResultMapper
    {
        public IResult Map<T>(Result<T> result, HttpContext httpContext) =>
            throw new NotSupportedException(
                "非 HTTP Worker 集成夹具不映射 API 结果。");

        public IResult MapException(
            Exception exception,
            HttpContext httpContext) =>
            throw new NotSupportedException(
                "非 HTTP Worker 集成夹具不映射 API 异常。");
    }

}
