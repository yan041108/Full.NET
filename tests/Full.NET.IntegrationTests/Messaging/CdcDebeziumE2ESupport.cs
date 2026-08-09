using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>
/// CDC → Debezium → Kafka → Inbox 真实链路测试辅助。
/// </summary>
internal static class CdcDebeziumE2ESupport
{
    internal static string GetShadowTopic(string messageType) =>
        $"{CdcShadowFixture.ShadowTopicPrefix}.{messageType}";

    internal static async Task<ConsumeResult<string, byte[]>?> TryConsumeShadowEventAsync(
        CdcDebeziumPipelineEnvironment pipeline,
        string topic,
        Guid eventId,
        TimeSpan timeout) =>
        await TryConsumeShadowEventAsync(
            pipeline,
            topic,
            eventId,
            $"fullnet.cdc.debezium.e2e.{Guid.NewGuid():N}",
            timeout).ConfigureAwait(false);

    internal static async Task<ConsumeResult<string, byte[]>?> TryConsumeShadowEventAsync(
        CdcDebeziumPipelineEnvironment pipeline,
        string topic,
        Guid eventId,
        string consumerGroupId,
        TimeSpan timeout)
    {
        using var consumer = CreateShadowConsumer(pipeline, consumerGroupId);
        consumer.Subscribe(topic);
        return await ConsumeShadowEventAsync(consumer, eventId, timeout).ConfigureAwait(false);
    }

    internal static IConsumer<string, byte[]> CreateShadowConsumer(
        CdcDebeziumPipelineEnvironment pipeline,
        string consumerGroupId) =>
        new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = pipeline.BootstrapServers,
            GroupId = consumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();

    private static async Task<ConsumeResult<string, byte[]>?> ConsumeShadowEventAsync(
        IConsumer<string, byte[]> consumer,
        Guid eventId,
        TimeSpan timeout) =>
        await Task.FromResult(ConsumeShadowEvent(consumer, eventId, timeout)).ConfigureAwait(false);

    private static ConsumeResult<string, byte[]>? ConsumeShadowEvent(
        IConsumer<string, byte[]> consumer,
        Guid eventId,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            ConsumeResult<string, byte[]>? consumed;
            try
            {
                consumed = consumer.Consume(TimeSpan.FromMilliseconds(500));
            }
            catch (ConsumeException exception)
                when (exception.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                continue;
            }

            if (consumed?.Message?.Value is null or { Length: 0 })
            {
                continue;
            }

            var reader = new KafkaEnvelopeReader();
            if (reader.TryRead(consumed, out var envelope, out _)
                && envelope is not null
                && envelope.EventId == eventId)
            {
                return consumed;
            }
        }

        return null;
    }

    internal static async Task<string> DescribeShadowKafkaDiagnosticsAsync(
        CdcDebeziumPipelineEnvironment pipeline,
        string expectedTopic,
        TimeSpan timeout)
    {
        var topics = await ListTopicsAsync(pipeline.BootstrapServers, CdcShadowFixture.ShadowTopicPrefix)
            .ConfigureAwait(false);
        var peek = await TryPeekTopicMessageAsync(pipeline.BootstrapServers, expectedTopic, timeout)
            .ConfigureAwait(false);
        return $"topics=[{string.Join(", ", topics)}]; peek={peek}";
    }

    internal static KafkaMessagingOptions CreateKafkaOptions(
        CdcDebeziumPipelineEnvironment pipeline,
        string clientId) =>
        new()
        {
            Enabled = true,
            BootstrapServers = pipeline.BootstrapServers,
            ClientId = clientId,
            ConsumerInstanceId = $"{clientId}-01",
            SecurityProtocol = "Plaintext",
            MessageMaxBytes = 1_048_576,
            RetryStages = ["5s", "1m", "15m"],
            DeliveryTimeoutMilliseconds = 30_000,
        };

    internal static KafkaRetryRouter CreateRetryRouter(
        CdcDebeziumPipelineEnvironment pipeline,
        string clientId)
    {
        var options = Options.Create(CreateKafkaOptions(pipeline, clientId));
        return new KafkaRetryRouter(
            options,
            new KafkaMessagingProducer(options),
            NullLogger<KafkaRetryRouter>.Instance);
    }

    internal static async Task<(InboxConsumeStatus First, InboxConsumeStatus Second)>
        ConsumeUncommittedRedeliveryThroughInboxAsync(
            CdcDebeziumPipelineEnvironment pipeline,
            DatabaseOptions options,
            string topic,
            Guid eventId,
            string consumerGroupId,
            TimeSpan timeout)
    {
        using var consumer = CreateShadowConsumer(pipeline, consumerGroupId);
        consumer.Subscribe(topic);
        var consumed = await ConsumeShadowEventAsync(consumer, eventId, timeout).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Expected Kafka message for uncommitted redelivery first consume.");
        var first = await ConsumeThroughInboxAsync(options, consumed).ConfigureAwait(false);

        // 未提交 Offset 时本地 position 已前进；Seek 回同一位点模拟 Broker 重投。
        consumer.Seek(consumed.TopicPartitionOffset);
        var redelivered = consumer.Consume(TimeSpan.FromSeconds(5))
            ?? throw new InvalidOperationException(
                "Expected Kafka message for uncommitted redelivery after seek.");
        var reader = new KafkaEnvelopeReader();
        if (!reader.TryRead(redelivered, out var envelope, out _)
            || envelope is null
            || envelope.EventId != eventId)
        {
            throw new InvalidOperationException(
                "Redelivered Kafka message does not match the expected event id.");
        }

        var second = await ConsumeThroughInboxAsync(options, redelivered).ConfigureAwait(false);
        return (first, second);
    }

    internal static async Task<ConsumeResult<string, byte[]>?> TryConsumeRetryTopicAsync(
        CdcDebeziumPipelineEnvironment pipeline,
        string retryTopic,
        Guid eventId,
        TimeSpan timeout)
    {
        using var consumer = CreateShadowConsumer(
            pipeline,
            $"fullnet.cdc.debezium.retry.{Guid.NewGuid():N}");
        consumer.Subscribe(retryTopic);
        return await ConsumeShadowEventAsync(consumer, eventId, timeout).ConfigureAwait(false);
    }

    internal static async Task<IReadOnlyList<string>> ListShadowTopicsAsync(
        CdcDebeziumPipelineEnvironment pipeline) =>
        await ListTopicsAsync(pipeline.BootstrapServers, CdcShadowFixture.ShadowTopicPrefix)
            .ConfigureAwait(false);

    private static async Task<IReadOnlyList<string>> ListTopicsAsync(
        string bootstrapServers,
        string prefix)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = bootstrapServers,
        }).Build();
        var metadata = admin.GetMetadata(TimeSpan.FromSeconds(10));
        return metadata.Topics
            .Where(topic => topic.Topic.StartsWith(prefix, StringComparison.Ordinal))
            .Select(topic => topic.Topic)
            .OrderBy(topic => topic, StringComparer.Ordinal)
            .ToArray();
    }

    private static Task<string> TryPeekTopicMessageAsync(
        string bootstrapServers,
        string topic,
        TimeSpan timeout) =>
        Task.Run(() =>
        {
            using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
            {
                BootstrapServers = bootstrapServers,
                GroupId = $"fullnet.cdc.debezium.peek.{Guid.NewGuid():N}",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false,
            }).Build();
            consumer.Subscribe(topic);
            var deadline = DateTime.UtcNow.Add(timeout);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var consumed = consumer.Consume(TimeSpan.FromMilliseconds(500));
                    if (consumed?.Message is null)
                    {
                        continue;
                    }

                    var headers = consumed.Message.Headers is null
                        ? string.Empty
                        : string.Join(
                            ", ",
                            consumed.Message.Headers.Select(header =>
                                $"{header.Key}={Encoding.UTF8.GetString(header.GetValueBytes() ?? [])}"));
                    return $"topic={consumed.Topic}; key={consumed.Message.Key}; headers=[{headers}]; valueLength={consumed.Message.Value?.Length ?? 0}";
                }
                catch (ConsumeException exception)
                    when (exception.Error.Code == ErrorCode.UnknownTopicOrPart)
                {
                }
            }

            return "no-message";
        });

    internal static async Task<InboxConsumeStatus> ConsumeThroughInboxAsync(
        DatabaseOptions options,
        ConsumeResult<string, byte[]> consumed)
    {
        var reader = new KafkaEnvelopeReader();
        if (!reader.TryRead(consumed, out var envelope, out var failureCode)
            || envelope is null)
        {
            throw new InvalidOperationException($"Kafka envelope invalid: {failureCode}");
        }

        var configuration = MessagingOutboxTestSupport.CreateConfiguration(options);
        await using var services = MessagingInboxTestSupport.BuildInboxServices(configuration);
        await using var scope = services.CreateAsyncScope();
        var downstreamPartitionKey = Guid.CreateVersion7().ToString("D");
        var subscription = new MessagingInboxTestSupport.DownstreamOutboxSubscription(
            scope.ServiceProvider.GetRequiredService<IOutboxWriter>(),
            downstreamPartitionKey);
        var dispatcher = MessagingInboxTestSupport.CreateDispatcher(scope, subscription);
        var result = await dispatcher.ConsumeAsync(
            MessagingInboxTestSupport.ConsumerName,
            envelope,
            subscription,
            CancellationToken.None);
        return result.Status;
    }

    internal static async Task<(InboxConsumeStatus First, InboxConsumeStatus Second)> ConsumeDuplicateThroughInboxAsync(
        DatabaseOptions options,
        ConsumeResult<string, byte[]> consumed)
    {
        var first = await ConsumeThroughInboxAsync(options, consumed).ConfigureAwait(false);
        var second = await ConsumeThroughInboxAsync(options, consumed).ConfigureAwait(false);
        return (first, second);
    }

    internal static async Task<CommittedOutboxDelivery> PublishAndWaitForShadowEventAsync(
        CdcDebeziumMySqlE2EScenario scenario,
        string connectorName,
        TimeSpan timeout)
    {
        var partitionKey = Guid.CreateVersion7().ToString("D");
        var committed = await CdcShadowFixture.InsertCommittedOutboxEventAsync(
            scenario.Options,
            partitionKey);
        var topic = GetShadowTopic(MessagingOutboxTestSupport.TestEventType);
        var consumed = await TryConsumeShadowEventAsync(
            scenario.Pipeline,
            topic,
            committed.Fingerprint.EventId,
            timeout).ConfigureAwait(false);
        if (consumed is null)
        {
            var connectorStatus = await scenario.ConnectAdmin
                .TryGetConnectorStatusAsync(connectorName)
                .ConfigureAwait(false);
            var kafkaDiagnostics = await DescribeShadowKafkaDiagnosticsAsync(
                scenario.Pipeline,
                topic,
                TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.Inconclusive(
                "Debezium did not publish a readable outbox event to Kafka within timeout. "
                + $"Connector status: {connectorStatus}. Kafka diagnostics: {kafkaDiagnostics}");
        }

        return new CommittedOutboxDelivery(committed, consumed);
    }

    internal sealed record CommittedOutboxDelivery(
        CdcShadowFixture.CommittedOutboxEvent Committed,
        ConsumeResult<string, byte[]> Consumed);
}
