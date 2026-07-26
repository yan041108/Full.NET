using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Realtime;
using Full.NET.Realtime.SignalR;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Full.NET.IntegrationTests.Realtime;

[TestClass]
[DoNotParallelize]
public sealed class RealtimeRedisBackplaneRecoveryTests
{
    [TestMethod]
    public async Task SqlServer_backplane_recovers_cross_node_delivery_without_restarting_hosts()
    {
        await VerifyBackplaneRecoveryAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_backplane_recovers_cross_node_delivery_without_restarting_hosts()
    {
        await VerifyBackplaneRecoveryAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    private static async Task VerifyBackplaneRecoveryAsync(
        DatabaseProvider provider,
        string connectionString)
    {
        var redisHostPort = ReserveFreeTcpPort();
        await using var redis = new RedisBuilder("redis:8.6")
            .WithPortBinding(redisHostPort, 6379)
            .Build();
        await redis.StartAsync();
        var redisEndpoint = redis.GetConnectionString();
        var redisConnectionString =
            $"{redisEndpoint},connectTimeout=1000,syncTimeout=1000";
        var settings = new Dictionary<string, string?>
        {
            [$"{RealtimeOptions.SectionName}:RedisBackplaneConnectionString"] =
                redisConnectionString,
        };
        using var subscriberFactory = new FullNetApiFactory(
            provider,
            connectionString,
            settings);
        using var publisherFactory = subscriberFactory.CreateIsolatedFactory();
        await subscriberFactory.InitializeAsync();
        await publisherFactory.InitializeAsync();
        using var subscriberHealthClient = subscriberFactory.CreateClientForHost("localhost");
        using var publisherHealthClient = publisherFactory.CreateClientForHost("localhost");
        await WaitForHealthStatusAsync(
            subscriberHealthClient,
            HttpStatusCode.OK,
            TimeSpan.FromSeconds(20));
        await WaitForHealthStatusAsync(
            publisherHealthClient,
            HttpStatusCode.OK,
            TimeSpan.FromSeconds(20));

        var identity = await subscriberFactory.CreateHostIdentityAsync(
            $"realtime-{Guid.NewGuid():N}",
            []);
        var receivedMessages = Channel.CreateUnbounded<RealtimeMessage>();
        await using var connection = new HubConnectionBuilder()
            .WithUrl(
                "http://localhost/hubs/notifications",
                options =>
                {
                    options.AccessTokenProvider = () =>
                        Task.FromResult<string?>(identity.AccessToken);
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ =>
                        subscriberFactory.Server.CreateHandler();
                })
            .Build();
        using var subscription = connection.On<RealtimeMessage>(
            "ReceiveMessageAsync",
            message => receivedMessages.Writer.TryWrite(message));
        await connection.StartAsync();

        var publisher = publisherFactory.Services
            .GetRequiredService<IRealtimePublisher>();
        await PublishUntilReceivedAsync(
            publisher,
            identity.UserId,
            receivedMessages.Reader,
            $"realtime.backplane.before.{Guid.NewGuid():N}",
            TimeSpan.FromSeconds(15));

        await redis.StopAsync();
        await WaitForHealthStatusAsync(
            subscriberHealthClient,
            HttpStatusCode.ServiceUnavailable,
            TimeSpan.FromSeconds(20));
        await WaitForHealthStatusAsync(
            publisherHealthClient,
            HttpStatusCode.ServiceUnavailable,
            TimeSpan.FromSeconds(20));

        var outageCode = $"realtime.backplane.outage.{Guid.NewGuid():N}";
        try
        {
            await publisher.PublishToUserAsync(
                identity.UserId,
                new RealtimeMessage(outageCode));
        }
        catch (RedisException)
        {
            // Redis 失联时允许发布端快速失败；关键不变量是不得伪造跨节点送达。
        }

        Assert.IsNull(
            await WaitForMessageAsync(
                receivedMessages.Reader,
                outageCode,
                TimeSpan.FromSeconds(2)),
            "Redis 失联期间不得把跨节点消息报告为已送达。");

        await redis.StartAsync();
        Assert.AreEqual(
            redisEndpoint,
            redis.GetConnectionString(),
            "Redis stop/start 必须保持宿主端点不变，才能验证 API 原连接的自动恢复。");
        await WaitForHealthStatusAsync(
            subscriberHealthClient,
            HttpStatusCode.OK,
            TimeSpan.FromSeconds(30));
        await WaitForHealthStatusAsync(
            publisherHealthClient,
            HttpStatusCode.OK,
            TimeSpan.FromSeconds(30));
        await PublishUntilReceivedAsync(
            publisher,
            identity.UserId,
            receivedMessages.Reader,
            $"realtime.backplane.recovered.{Guid.NewGuid():N}",
            TimeSpan.FromSeconds(30));

        Assert.AreEqual(HubConnectionState.Connected, connection.State);
    }

    private static async Task PublishUntilReceivedAsync(
        IRealtimePublisher publisher,
        Guid userId,
        ChannelReader<RealtimeMessage> reader,
        string code,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                await publisher.PublishToUserAsync(
                    userId,
                    new RealtimeMessage(code));
            }
            catch (RedisException)
            {
                // Backplane 重连窗口内允许单次发布失败，直到同一宿主恢复可用。
            }

            if (await WaitForMessageAsync(
                    reader,
                    code,
                    TimeSpan.FromMilliseconds(750)) is not null)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        Assert.Fail($"等待跨节点实时消息超时：{code}");
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

    private static async Task WaitForHealthStatusAsync(
        HttpClient client,
        HttpStatusCode expectedStatus,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        HttpStatusCode? lastStatus = null;
        while (stopwatch.Elapsed < timeout)
        {
            using var response = await client.GetAsync(
                "/health/ready",
                HttpCompletionOption.ResponseHeadersRead);
            lastStatus = response.StatusCode;
            if (lastStatus == expectedStatus)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        Assert.Fail(
            $"等待 /health/ready={expectedStatus} 超时，最后状态为 {lastStatus}。");
    }

    private static int ReserveFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
