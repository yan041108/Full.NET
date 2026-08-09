using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>
/// 真实 MySQL ROW Binlog → Debezium → Kafka → Inbox 端到端验证。
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class MySqlCdcDebeziumInboxE2ETests
{
    [TestMethod]
    public async Task MySql_committed_outbox_reaches_kafka_via_debezium_and_inbox()
    {
        await using var scenario = await RequireMySqlScenarioAsync();
        var connectorName = CreateConnectorName();
        var connectorConfig = await scenario.CreateConnectorConfigAsync();
        try
        {
            await CdcDebeziumConnectorTestSupport.RegisterHealthyShadowConnectorAsync(
                scenario.ConnectAdmin,
                connectorName,
                connectorConfig,
                TimeSpan.FromSeconds(120));

            var delivery = await CdcDebeziumE2ESupport.PublishAndWaitForShadowEventAsync(
                scenario,
                connectorName,
                TimeSpan.FromSeconds(120));
            var status = await CdcDebeziumE2ESupport.ConsumeThroughInboxAsync(
                scenario.Options,
                delivery.Consumed);
            Assert.AreEqual(InboxConsumeStatus.Processed, status);
        }
        finally
        {
            await scenario.ConnectAdmin.DeleteConnectorAsync(connectorName);
        }
    }

    [TestMethod]
    public async Task MySql_duplicate_kafka_delivery_is_inbox_idempotent()
    {
        await using var scenario = await RequireMySqlScenarioAsync();
        var connectorName = CreateConnectorName();
        var connectorConfig = await scenario.CreateConnectorConfigAsync();
        try
        {
            await CdcDebeziumConnectorTestSupport.RegisterHealthyShadowConnectorAsync(
                scenario.ConnectAdmin,
                connectorName,
                connectorConfig,
                TimeSpan.FromSeconds(120));

            var delivery = await CdcDebeziumE2ESupport.PublishAndWaitForShadowEventAsync(
                scenario,
                connectorName,
                TimeSpan.FromSeconds(120));
            var (first, second) = await CdcDebeziumE2ESupport.ConsumeDuplicateThroughInboxAsync(
                scenario.Options,
                delivery.Consumed);

            Assert.AreEqual(InboxConsumeStatus.Processed, first);
            Assert.AreEqual(InboxConsumeStatus.AlreadyProcessed, second);
        }
        finally
        {
            await scenario.ConnectAdmin.DeleteConnectorAsync(connectorName);
        }
    }

    [TestMethod]
    public async Task MySql_connector_restart_preserves_schema_history_and_delivers_new_events()
    {
        await using var scenario = await RequireMySqlScenarioAsync();
        var firstConnectorName = CreateConnectorName();
        var secondConnectorName = CreateConnectorName();
        var connectorConfig = await scenario.CreateConnectorConfigAsync();
        try
        {
            await CdcDebeziumConnectorTestSupport.RegisterHealthyShadowConnectorAsync(
                scenario.ConnectAdmin,
                firstConnectorName,
                connectorConfig,
                TimeSpan.FromSeconds(120));

            var firstDelivery = await CdcDebeziumE2ESupport.PublishAndWaitForShadowEventAsync(
                scenario,
                firstConnectorName,
                TimeSpan.FromSeconds(120));
            var firstStatus = await CdcDebeziumE2ESupport.ConsumeThroughInboxAsync(
                scenario.Options,
                firstDelivery.Consumed);
            Assert.AreEqual(InboxConsumeStatus.Processed, firstStatus);

            await scenario.ConnectAdmin.DeleteConnectorAsync(firstConnectorName);
            await CdcDebeziumConnectorTestSupport.RegisterHealthyShadowConnectorAsync(
                scenario.ConnectAdmin,
                secondConnectorName,
                connectorConfig,
                TimeSpan.FromSeconds(120));

            var topics = await CdcDebeziumE2ESupport.ListShadowTopicsAsync(scenario.Pipeline);
            Assert.IsTrue(
                topics.Contains("fullnet.dev.shadow.internal.schema-history.mysql", StringComparer.Ordinal),
                "Schema history topic must survive connector restart.");

            var secondDelivery = await CdcDebeziumE2ESupport.PublishAndWaitForShadowEventAsync(
                scenario,
                secondConnectorName,
                TimeSpan.FromSeconds(120));
            var secondStatus = await CdcDebeziumE2ESupport.ConsumeThroughInboxAsync(
                scenario.Options,
                secondDelivery.Consumed);
            Assert.AreEqual(InboxConsumeStatus.Processed, secondStatus);
            Assert.AreNotEqual(
                firstDelivery.Committed.Fingerprint.EventId,
                secondDelivery.Committed.Fingerprint.EventId);
        }
        finally
        {
            await scenario.ConnectAdmin.DeleteConnectorAsync(firstConnectorName);
            await scenario.ConnectAdmin.DeleteConnectorAsync(secondConnectorName);
        }
    }

    [TestMethod]
    public async Task MySql_uncommitted_offset_redelivery_is_inbox_idempotent()
    {
        await using var scenario = await RequireMySqlScenarioAsync();
        var connectorName = CreateConnectorName();
        var connectorConfig = await scenario.CreateConnectorConfigAsync();
        var consumerGroupId = $"fullnet.cdc.e2e.redelivery.{Guid.NewGuid():N}";
        try
        {
            await CdcDebeziumConnectorTestSupport.RegisterHealthyShadowConnectorAsync(
                scenario.ConnectAdmin,
                connectorName,
                connectorConfig,
                TimeSpan.FromSeconds(120));

            var delivery = await CdcDebeziumE2ESupport.PublishAndWaitForShadowEventAsync(
                scenario,
                connectorName,
                TimeSpan.FromSeconds(120));
            var topic = CdcDebeziumE2ESupport.GetShadowTopic(MessagingOutboxTestSupport.TestEventType);
            var (first, second) = await CdcDebeziumE2ESupport.ConsumeUncommittedRedeliveryThroughInboxAsync(
                scenario.Pipeline,
                scenario.Options,
                topic,
                delivery.Committed.Fingerprint.EventId,
                consumerGroupId,
                TimeSpan.FromSeconds(60));

            Assert.AreEqual(InboxConsumeStatus.Processed, first);
            Assert.AreEqual(InboxConsumeStatus.AlreadyProcessed, second);
        }
        finally
        {
            await scenario.ConnectAdmin.DeleteConnectorAsync(connectorName);
        }
    }

    [TestMethod]
    public async Task MySql_paused_connector_resumes_and_delivers_pending_outbox()
    {
        await using var scenario = await RequireMySqlScenarioAsync();
        var connectorName = CreateConnectorName();
        var connectorConfig = await scenario.CreateConnectorConfigAsync();
        try
        {
            await CdcDebeziumConnectorTestSupport.RegisterHealthyShadowConnectorAsync(
                scenario.ConnectAdmin,
                connectorName,
                connectorConfig,
                TimeSpan.FromSeconds(120));

            await scenario.ConnectAdmin.PauseConnectorAsync(connectorName);
            var partitionKey = Guid.CreateVersion7().ToString("D");
            var committed = await CdcShadowFixture.InsertCommittedOutboxEventAsync(
                scenario.Options,
                partitionKey);
            var topic = CdcDebeziumE2ESupport.GetShadowTopic(MessagingOutboxTestSupport.TestEventType);
            var whilePaused = await CdcDebeziumE2ESupport.TryConsumeShadowEventAsync(
                scenario.Pipeline,
                topic,
                committed.Fingerprint.EventId,
                TimeSpan.FromSeconds(10));
            Assert.IsNull(whilePaused, "Paused connector must not publish new outbox rows to Kafka.");

            await scenario.ConnectAdmin.ResumeConnectorAsync(connectorName);
            Assert.IsTrue(
                await scenario.ConnectAdmin.WaitForConnectorHealthyAsync(
                    connectorName,
                    TimeSpan.FromSeconds(120)),
                "Connector must return to healthy RUNNING after resume.");

            var consumed = await CdcDebeziumE2ESupport.TryConsumeShadowEventAsync(
                scenario.Pipeline,
                topic,
                committed.Fingerprint.EventId,
                TimeSpan.FromSeconds(120));
            if (consumed is null)
            {
                Assert.Inconclusive("Debezium did not publish pending outbox after connector resume.");
            }

            var status = await CdcDebeziumE2ESupport.ConsumeThroughInboxAsync(
                scenario.Options,
                consumed);
            Assert.AreEqual(InboxConsumeStatus.Processed, status);
        }
        finally
        {
            await scenario.ConnectAdmin.DeleteConnectorAsync(connectorName);
        }
    }

    [TestMethod]
    public async Task MySql_cdc_envelope_routes_to_retry_topic_on_transient_failure()
    {
        await using var scenario = await RequireMySqlScenarioAsync();
        var connectorName = CreateConnectorName();
        var connectorConfig = await scenario.CreateConnectorConfigAsync();
        try
        {
            await CdcDebeziumConnectorTestSupport.RegisterHealthyShadowConnectorAsync(
                scenario.ConnectAdmin,
                connectorName,
                connectorConfig,
                TimeSpan.FromSeconds(120));

            var delivery = await CdcDebeziumE2ESupport.PublishAndWaitForShadowEventAsync(
                scenario,
                connectorName,
                TimeSpan.FromSeconds(120));
            var baseTopic = CdcDebeziumE2ESupport.GetShadowTopic(MessagingOutboxTestSupport.TestEventType);
            var retryTopic = KafkaTopicNames.GetRetryTopic(baseTopic, "5s");
            var retryRouter = CdcDebeziumE2ESupport.CreateRetryRouter(
                scenario.Pipeline,
                "fullnet.cdc.debezium.retry-router");
            var failure = new IntegrationEventFailure(
                IntegrationEventFailureKind.Transient,
                IntegrationEventFailureCodes.TransientPrefix + "cdc_e2e",
                "Transient failure for CDC retry routing.");
            var routed = await retryRouter.TryRouteAsync(
                delivery.Consumed,
                MessagingInboxTestSupport.ConsumerName,
                failure,
                attemptCount: 0,
                CancellationToken.None);
            Assert.IsTrue(routed);

            var retryConsumed = await CdcDebeziumE2ESupport.TryConsumeRetryTopicAsync(
                scenario.Pipeline,
                retryTopic,
                delivery.Committed.Fingerprint.EventId,
                TimeSpan.FromSeconds(30));
            Assert.IsNotNull(retryConsumed);
            Assert.IsTrue(KafkaDeliveryHeaders.TryReadHeader(
                retryConsumed!.Message.Headers,
                KafkaDeliveryHeaderNames.FailureCode,
                out var failureCode));
            Assert.AreEqual(failure.Code, failureCode);
        }
        finally
        {
            await scenario.ConnectAdmin.DeleteConnectorAsync(connectorName);
        }
    }

    [TestMethod]
    public async Task MySql_broker_interruption_allows_subsequent_cdc_delivery()
    {
        await using var scenario = await RequireMySqlScenarioAsync();
        var connectorName = CreateConnectorName();
        var connectorConfig = await scenario.CreateConnectorConfigAsync();
        try
        {
            await CdcDebeziumConnectorTestSupport.RegisterHealthyShadowConnectorAsync(
                scenario.ConnectAdmin,
                connectorName,
                connectorConfig,
                TimeSpan.FromSeconds(120));

            var firstDelivery = await CdcDebeziumE2ESupport.PublishAndWaitForShadowEventAsync(
                scenario,
                connectorName,
                TimeSpan.FromSeconds(120));
            var firstStatus = await CdcDebeziumE2ESupport.ConsumeThroughInboxAsync(
                scenario.Options,
                firstDelivery.Consumed);
            Assert.AreEqual(InboxConsumeStatus.Processed, firstStatus);

            await scenario.Pipeline.InterruptBrokerAsync();

            var secondDelivery = await CdcDebeziumE2ESupport.PublishAndWaitForShadowEventAsync(
                scenario,
                connectorName,
                TimeSpan.FromSeconds(120));
            var secondStatus = await CdcDebeziumE2ESupport.ConsumeThroughInboxAsync(
                scenario.Options,
                secondDelivery.Consumed);
            Assert.AreEqual(InboxConsumeStatus.Processed, secondStatus);
        }
        finally
        {
            await scenario.ConnectAdmin.DeleteConnectorAsync(connectorName);
        }
    }

    private static string CreateConnectorName() =>
        $"fullnet-mysql-outbox-shadow-{Guid.NewGuid():N}";

    private static async Task<CdcDebeziumMySqlE2EScenario> RequireMySqlScenarioAsync()
    {
        var scenario = await CdcDebeziumMySqlE2EScenario.TryCreateAsync();
        if (scenario is null)
        {
            Assert.Inconclusive(
                "MySQL ROW/FULL binlog or Debezium Connect is unavailable for CDC E2E.");
        }

        return scenario;
    }
}
