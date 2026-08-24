using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Realtime;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Full.NET.IntegrationTests.NativeAot;

[TestClass]
[DoNotParallelize]
public sealed class NativeApiSignalRJsonE2ETests
{
    [TestMethod]
    public async Task SqlServer_native_artifact_supports_json_signalr_with_redis_backplane()
    {
        _ = NativeApiArtifactLocator.RequireArtifact();
        await VerifySignalRJsonFlowAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_native_artifact_supports_json_signalr_with_redis_backplane()
    {
        _ = NativeApiArtifactLocator.RequireArtifact();
        await VerifySignalRJsonFlowAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    private static async Task VerifySignalRJsonFlowAsync(
        DatabaseProvider provider,
        string connectionString)
    {
        if (!NativeApiArtifactLocator.TryResolve(out var artifact, out var skipReason))
        {
            Assert.Inconclusive(skipReason ?? "Native AOT artifact unavailable.");
        }

        var redisConnectionString =
            await SharedDatabaseFixture.GetRedisConnectionStringAsync();
        var settings = new Dictionary<string, string?>
        {
            ["Realtime:RedisBackplaneConnectionString"] = redisConnectionString,
            ["ConnectionStrings:redis"] = redisConnectionString,
        };

        await NativeApiDatabaseBootstrap.BootstrapAsync(
            provider,
            connectionString);

        await using var host = await NativeApiProcessHost.StartAsync(
            artifact,
            provider,
            connectionString,
            settings,
            TimeSpan.FromMinutes(2));

        using var client = host.CreateClient();
        var token = await NativeApiE2EAssertions.LoginAsync(
            client,
            host.LogFilePath,
            CancellationToken.None);

        var receivedMessages = Channel.CreateUnbounded<RealtimeMessage>();
        var hubUrl = new Uri(host.BaseAddress, "hubs/notifications");
        await using var connection = new HubConnectionBuilder()
            .WithUrl(
                hubUrl,
                options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                    options.Transports = HttpTransportType.LongPolling;
                })
            .Build();
        connection.On<RealtimeMessage>(
            "ReceiveMessageAsync",
            message => receivedMessages.Writer.TryWrite(message));
        await connection.StartAsync();

        using var probeRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/realtime/probes/self");
        probeRequest.Headers.Add("Origin", "http://localhost");
        probeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var probeResponse = await client.SendAsync(probeRequest);
        Assert.AreEqual(HttpStatusCode.OK, probeResponse.StatusCode);

        var received = await WaitForMessageAsync(
            receivedMessages.Reader,
            RealtimeMessageCodes.ProbeSelf,
            TimeSpan.FromSeconds(15));
        Assert.IsNotNull(received);
        Assert.AreEqual(RealtimeMessageCodes.ProbeSelf, received.Code);
        Assert.IsNotNull(received.Data);
        Assert.IsNotNull(ReadGuid(received.Data, "probeId"));
        Assert.IsFalse(string.IsNullOrWhiteSpace(ReadString(received.Data, "hubPath")));
        Assert.AreEqual(1L, ReadInt64(received.Data, "sequence"));

        using var negotiateRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/hubs/notifications/negotiate?negotiateVersion=1");
        negotiateRequest.Headers.Add("Origin", "http://localhost");
        negotiateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var negotiateResponse = await client.SendAsync(negotiateRequest);
        Assert.AreEqual(HttpStatusCode.OK, negotiateResponse.StatusCode);
        using var negotiatePayload = JsonDocument.Parse(
            await negotiateResponse.Content.ReadAsStringAsync());
        Assert.IsTrue(negotiatePayload.RootElement.TryGetProperty("connectionId", out _));

        await host.StopGracefullyAsync();
        host.AssertNoFatalMarkersInLogs();
    }

    private static async Task<RealtimeMessage?> WaitForMessageAsync(
        ChannelReader<RealtimeMessage> reader,
        string code,
        TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            while (await reader.WaitToReadAsync(cts.Token))
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
        catch (OperationCanceledException)
        {
        }

        return null;
    }

    private static Guid? ReadGuid(IReadOnlyDictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            Guid guid => guid,
            string text when Guid.TryParse(text, out var parsed) => parsed,
            JsonElement element when element.ValueKind == JsonValueKind.String
                && Guid.TryParse(element.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> data, string key) =>
        data.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static long ReadInt64(IReadOnlyDictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return 0;
        }

        return value switch
        {
            long number => number,
            int intNumber => intNumber,
            JsonElement element when element.ValueKind == JsonValueKind.Number =>
                element.GetInt64(),
            _ => long.TryParse(value.ToString(), out var parsed) ? parsed : 0,
        };
    }
}
