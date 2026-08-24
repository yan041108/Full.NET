using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Confluent.Kafka;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Messaging;
using Full.NET.Modules.Messaging.Contracts;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>
/// Native Host.Api 通过真实 librdkafka 消费 Kafka 并执行范围重放的 HTTP 断言。
/// </summary>
internal static class NativeApiKafkaReplayE2EAssertions
{
    public static async Task VerifyKafkaReplayHttpFlowAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var artifact = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiDatabaseBootstrap.BootstrapAsync(
            provider,
            connectionString,
            cancellationToken).ConfigureAwait(false);
        await NativeApiKafkaReplaySupport.EnsureCdcKafkaOwnershipAsync(
            provider,
            connectionString,
            cancellationToken).ConfigureAwait(false);

        var kafka = await KafkaFixture.GetOrStartAsync().ConfigureAwait(false);
        await kafka.EnsureTopicsAsync(MessagingInboxTestSupport.TopicCode)
            .ConfigureAwait(false);

        var eventId = Guid.CreateVersion7();
        DeliveryResult<string, byte[]> delivery;
        using (var producer = kafka.CreateProducer(
                   $"fullnet.native.kafka.replay.{provider.ToString().ToLowerInvariant()}"))
        {
            delivery = await producer.ProduceAsync(
                    MessagingInboxTestSupport.TopicCode,
                    KafkaTestMessages.Create(
                        MessagingInboxTestSupport.TopicCode,
                        $"native-replay-{eventId:N}",
                        [0x01],
                        MessagingOutboxTestSupport.TestEventType,
                        eventId),
                    cancellationToken)
                .ConfigureAwait(false);
            producer.Flush(TimeSpan.FromSeconds(10));
        }

        var kafkaOptions = kafka.CreateOptions(
            $"fullnet.native.host.{provider.ToString().ToLowerInvariant()}");
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Messaging:Kafka:Enabled"] = "true",
            ["Messaging:Kafka:BootstrapServers"] = kafkaOptions.BootstrapServers,
            ["Messaging:Kafka:ClientId"] = kafkaOptions.ClientId,
            ["Messaging:Kafka:SecurityProtocol"] = kafkaOptions.SecurityProtocol,
            ["Messaging:KafkaReplay:Enabled"] = "true",
            ["Messaging:KafkaReplay:MaximumSynchronousMessages"] = "10",
            ["Messaging:KafkaReplay:ExecutionTimeoutSeconds"] = "45",
        };

        await using var host = await NativeApiProcessHost.StartAsync(
            artifact,
            provider,
            connectionString,
            settings,
            TimeSpan.FromMinutes(3),
            cancellationToken).ConfigureAwait(false);

        using var client = host.CreateClient();
        var token = await NativeApiE2EAssertions.LoginAsync(client, cancellationToken)
            .ConfigureAwait(false);

        var replayBody = new KafkaRangeReplayRequest(
            MessagingInboxTestSupport.TopicCode,
            FromTimestampUtc: null,
            ToTimestampUtc: null,
            FromOffset: delivery.Offset.Value,
            ToOffset: delivery.Offset.Value,
            Partitions: [delivery.Partition.Value],
            MessagingInboxTestSupport.ConsumerName,
            MaxMessages: 1,
            Reason: "native kafka replay e2e");

        using var firstRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/messaging/kafka/replay",
            token,
            replayBody);
        using var firstResponse = await client.SendAsync(firstRequest, cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, firstResponse.StatusCode);
        var first = await firstResponse.Content.ReadFromJsonAsync<KafkaRangeReplayResponse>(
                cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(first);
        Assert.AreEqual(1, first.ProcessedMessages);
        Assert.AreEqual(0, first.AlreadyProcessedMessages);

        using var duplicateRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/messaging/kafka/replay",
            token,
            replayBody);
        using var duplicateResponse = await client.SendAsync(duplicateRequest, cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, duplicateResponse.StatusCode);
        var duplicate = await duplicateResponse.Content.ReadFromJsonAsync<KafkaRangeReplayResponse>(
                cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(duplicate);
        Assert.AreEqual(0, duplicate.ProcessedMessages);
        Assert.AreEqual(1, duplicate.AlreadyProcessedMessages);

        await host.StopGracefullyAsync(cancellationToken).ConfigureAwait(false);
        host.AssertNoFatalMarkersInLogs();
    }

    private static HttpRequestMessage CreateBearerJsonRequest(
        HttpMethod method,
        string path,
        string token,
        object body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
