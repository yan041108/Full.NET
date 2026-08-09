using System.Text;
using Confluent.Kafka;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;

namespace Full.NET.IntegrationTests.Messaging;

[TestClass]
[DoNotParallelize]
public sealed class KafkaFailureRecoveryTests
{
    [TestMethod]
    public async Task Transient_failure_routes_message_to_first_retry_topic()
    {
        var environment = await KafkaFixture.GetOrStartAsync().ConfigureAwait(false);
        var topic = $"fullnet.test.recovery.transient.{Guid.NewGuid():N}.v1";
        var retryTopic = KafkaTopicNames.GetRetryTopic(topic, "5s");
        await environment.EnsureTopicsAsync(topic, retryTopic).ConfigureAwait(false);
        using var producer = environment.CreateProducer("fullnet.kafka.test.producer");
        var source = KafkaTestMessages.Create(topic, "retry-key", [0x21]);
        await producer.ProduceAsync(topic, source).ConfigureAwait(false);
        producer.Flush(TimeSpan.FromSeconds(10));

        using var consumer = environment.CreateConsumer("fullnet.kafka.test.retry-source", "fullnet.kafka.test.retry-source");
        consumer.Subscribe(topic);
        var consumed = await KafkaTestMessages.ConsumeOneAsync(consumer, TimeSpan.FromSeconds(30)).ConfigureAwait(false);

        var failure = new IntegrationEventFailure(
            IntegrationEventFailureKind.Transient,
            IntegrationEventFailureCodes.TransientPrefix + "broker_or_io",
            "Transient broker or I/O failure.");
        var retryRouter = environment.CreateRetryRouter("fullnet.kafka.test.retry-router");
        var routed = await retryRouter.TryRouteAsync(
                consumed,
                "fullnet.messaging.kafka.test",
                failure,
                attemptCount: 0,
                CancellationToken.None)
            .ConfigureAwait(false);
        Assert.IsTrue(routed);
        consumer.Commit(consumed);

        using var retryConsumer = environment.CreateConsumer("fullnet.kafka.test.retry-target", "fullnet.kafka.test.retry-target");
        retryConsumer.Subscribe(retryTopic);
        var retryResult = await KafkaTestMessages.ConsumeOneAsync(retryConsumer, TimeSpan.FromSeconds(30)).ConfigureAwait(false);

        Assert.AreEqual(source.Key, retryResult.Message.Key);
        CollectionAssert.AreEqual(source.Value, retryResult.Message.Value);
        Assert.IsTrue(KafkaDeliveryHeaders.TryReadHeader(
            retryResult.Message.Headers,
            KafkaDeliveryHeaderNames.FailureCode,
            out var failureCode));
        Assert.AreEqual(failure.Code, failureCode);
        Assert.IsTrue(KafkaDeliveryHeaders.TryReadRetryNotBeforeUtc(
            retryResult.Message.Headers,
            out var retryNotBeforeUtc));
        Assert.IsTrue(retryNotBeforeUtc >= DateTimeOffset.UtcNow.AddSeconds(3));
    }

    [TestMethod]
    public async Task Permanent_failure_publishes_to_dlq_and_commits_source_offset()
    {
        var environment = await KafkaFixture.GetOrStartAsync().ConfigureAwait(false);
        var topic = $"fullnet.test.recovery.dlq.{Guid.NewGuid():N}.v1";
        var deadLetterTopic = KafkaTopicNames.GetDeadLetterTopic(topic);
        await environment.EnsureTopicsAsync(topic, deadLetterTopic).ConfigureAwait(false);
        using var producer = environment.CreateProducer("fullnet.kafka.test.producer");
        var source = KafkaTestMessages.Create(topic, "dlq-key", [0x42]);
        await producer.ProduceAsync(topic, source).ConfigureAwait(false);
        producer.Flush(TimeSpan.FromSeconds(10));

        using var consumer = environment.CreateConsumer("fullnet.kafka.test.dlq-source", "fullnet.kafka.test.dlq-source");
        consumer.Subscribe(topic);
        var consumed = await KafkaTestMessages.ConsumeOneAsync(consumer, TimeSpan.FromSeconds(30)).ConfigureAwait(false);

        var failure = new IntegrationEventFailure(
            IntegrationEventFailureKind.Contract,
            IntegrationEventFailureCodes.MessageIdPayloadMismatch,
            "Permanent contract failure.");
        var publisher = environment.CreateDeadLetterPublisher("fullnet.kafka.test.dlq-publisher");
        var published = await publisher.TryPublishAsync(
                consumed,
                "fullnet.messaging.kafka.test",
                failure,
                attemptCount: 1,
                CancellationToken.None)
            .ConfigureAwait(false);
        Assert.IsTrue(published);
        consumer.Commit(consumed);

        using var deadLetterConsumer = environment.CreateConsumer("fullnet.kafka.test.dlq-target", "fullnet.kafka.test.dlq-target");
        deadLetterConsumer.Subscribe(deadLetterTopic);
        var deadLetter = await KafkaTestMessages.ConsumeOneAsync(deadLetterConsumer, TimeSpan.FromSeconds(30)).ConfigureAwait(false);

        Assert.AreEqual(source.Key, deadLetter.Message.Key);
        CollectionAssert.AreEqual(source.Value, deadLetter.Message.Value);
        Assert.IsTrue(KafkaDeliveryHeaders.TryReadHeader(
            deadLetter.Message.Headers,
            KafkaDeliveryHeaderNames.ConsumerName,
            out var consumerName));
        Assert.AreEqual("fullnet.messaging.kafka.test", consumerName);
        Assert.IsTrue(KafkaDeliveryHeaders.TryReadHeader(
            deadLetter.Message.Headers,
            KafkaDeliveryHeaderNames.SourceTopic,
            out var sourceTopic));
        Assert.AreEqual(topic, sourceTopic);
    }

    [TestMethod]
    public async Task Dead_letter_publish_failure_leaves_source_offset_uncommitted()
    {
        var environment = await KafkaFixture.GetOrStartAsync().ConfigureAwait(false);
        var topic = $"fullnet.test.recovery.dlq-fail.{Guid.NewGuid():N}.v1";
        await environment.EnsureTopicsAsync(topic).ConfigureAwait(false);
        using var producer = environment.CreateProducer("fullnet.kafka.test.producer");
        await producer.ProduceAsync(topic, KafkaTestMessages.Create(topic, "dlq-fail-key", [0x55])).ConfigureAwait(false);
        producer.Flush(TimeSpan.FromSeconds(10));

        using var consumer = environment.CreateConsumer("fullnet.kafka.test.dlq-fail", "fullnet.kafka.test.dlq-fail");
        consumer.Subscribe(topic);
        var consumed = await KafkaTestMessages.ConsumeOneAsync(consumer, TimeSpan.FromSeconds(30)).ConfigureAwait(false);

        var brokenOptions = Microsoft.Extensions.Options.Options.Create(new KafkaMessagingOptions
        {
            Enabled = true,
            BootstrapServers = "127.0.0.1:1",
            ClientId = "broken",
            SecurityProtocol = "Plaintext",
            RetryStages = ["5s"],
            DeliveryTimeoutMilliseconds = 1_000,
        });
        var brokenPublisher = new KafkaDeadLetterPublisher(
            brokenOptions,
            new KafkaMessagingProducer(brokenOptions),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<KafkaDeadLetterPublisher>.Instance);

        var failure = new IntegrationEventFailure(
            IntegrationEventFailureKind.Contract,
            IntegrationEventFailureCodes.PayloadRequired,
            "Permanent contract failure.");
        var published = await brokenPublisher.TryPublishAsync(
                consumed,
                "fullnet.messaging.kafka.test",
                failure,
                attemptCount: 0,
                CancellationToken.None)
            .ConfigureAwait(false);
        Assert.IsFalse(published);

        // Consume 会推进本地 position；未提交只保证组 offset 未前移，当前实例要立即重试仍需回退。
        consumer.Seek(consumed.TopicPartitionOffset);
        var redelivered = consumer.Consume(TimeSpan.FromMilliseconds(500));
        Assert.IsNotNull(redelivered);
        Assert.AreEqual(0x55, redelivered!.Message.Value![0]);
    }

    [TestMethod]
    public async Task Shutdown_cancellation_stops_consumer_poll_loop()
    {
        var environment = await KafkaFixture.GetOrStartAsync().ConfigureAwait(false);
        var topic = $"fullnet.test.recovery.shutdown.{Guid.NewGuid():N}.v1";
        await environment.EnsureTopicsAsync(topic).ConfigureAwait(false);
        using var consumer = environment.CreateConsumer("fullnet.kafka.test.shutdown", "fullnet.kafka.test.shutdown");
        consumer.Subscribe(topic);

        using var cancellation = new CancellationTokenSource();
        var pollTask = Task.Run(() =>
        {
            while (!cancellation.Token.IsCancellationRequested)
            {
                try
                {
                    consumer.Consume(cancellation.Token);
                }
                catch (ConsumeException exception) when (!exception.Error.IsFatal)
                {
                    continue;
                }
                catch (OperationCanceledException) when (cancellation.Token.IsCancellationRequested)
                {
                    break;
                }
            }
        });

        cancellation.CancelAfter(TimeSpan.FromMilliseconds(200));
        await pollTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        Assert.IsTrue(pollTask.IsCompletedSuccessfully);
    }

    [TestMethod]
    public async Task Consumer_recovers_after_broker_interruption()
    {
        await using var localEnvironment = await KafkaTestEnvironment.StartAsync().ConfigureAwait(false);
        var topic = $"fullnet.test.recovery.restart.{Guid.NewGuid():N}.v1";
        await localEnvironment.EnsureTopicsAsync(topic).ConfigureAwait(false);
        using var producer = localEnvironment.CreateProducer("fullnet.kafka.test.restart-producer");
        await producer.ProduceAsync(topic, KafkaTestMessages.Create(topic, "restart-key", [0x99])).ConfigureAwait(false);
        producer.Flush(TimeSpan.FromSeconds(10));

        using var consumer = localEnvironment.CreateConsumer("fullnet.kafka.test.restart", "fullnet.kafka.test.restart");
        consumer.Subscribe(topic);
        var first = await KafkaTestMessages.ConsumeOneAsync(consumer, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        Assert.AreEqual(0x99, first.Message.Value![0]);
        consumer.Commit(first);

        await localEnvironment.InterruptBrokerAsync().ConfigureAwait(false);

        using var producerAfterRestart = localEnvironment.CreateProducer("fullnet.kafka.test.restart-producer-2");
        await producerAfterRestart.ProduceAsync(topic, KafkaTestMessages.Create(topic, "restart-key", [0x9A])).ConfigureAwait(false);
        producerAfterRestart.Flush(TimeSpan.FromSeconds(30));

        var second = await KafkaTestMessages.ConsumeOneAsync(consumer, TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        Assert.AreEqual(0x9A, second.Message.Value![0]);
    }
}
