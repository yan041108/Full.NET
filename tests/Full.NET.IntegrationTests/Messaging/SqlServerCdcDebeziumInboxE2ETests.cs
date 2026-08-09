using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>
/// 真实 SQL Server CDC → Debezium → Kafka → Inbox 端到端验证。
/// 环境不可用（CDC Agent、Connect 或 Binlog）时必须 Inconclusive，禁止 mock Produce 冒充 CDC。
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class SqlServerCdcDebeziumInboxE2ETests
{
    [TestMethod]
    public async Task SqlServer_committed_outbox_reaches_kafka_via_debezium_and_inbox()
    {
        var pipeline = await CdcDebeziumPipelineFixture.GetOrStartAsync();
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var options = new DatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
            ConnectionString = connectionString,
            CommandTimeoutSeconds = 300,
        };
        await MessagingOutboxTestSupport.MigrateAsync(options);

        if (!await CdcShadowFixture.TryEnableSqlServerCdcAsync(connectionString))
        {
            Assert.Inconclusive(
                "SQL Server CDC could not be enabled in the test container (Agent/capture job gap).");
        }

        using var connectAdmin = pipeline.CreateConnectAdminClient();
        if (!await connectAdmin.WaitUntilReadyAsync(TimeSpan.FromSeconds(60)))
        {
            Assert.Inconclusive("Debezium Connect did not become ready within timeout.");
        }

        var connectorName = $"fullnet-sqlserver-outbox-shadow-{Guid.NewGuid():N}";
        var connectorConfig = await DebeziumConnectorTemplateFactory.CreateSqlServerShadowConfigAsync(
            connectionString,
            pipeline.HostGateway,
            pipeline.InternalKafkaBootstrapServers);
        try
        {
            await CdcDebeziumConnectorTestSupport.RegisterHealthyShadowConnectorAsync(
                connectAdmin,
                connectorName,
                connectorConfig,
                TimeSpan.FromSeconds(120));

            var partitionKey = Guid.CreateVersion7().ToString("D");
            var committed = await CdcShadowFixture.InsertCommittedOutboxEventAsync(options, partitionKey);
            var topic = CdcDebeziumE2ESupport.GetShadowTopic(MessagingOutboxTestSupport.TestEventType);

            if (!await CdcShadowFixture.WaitForSqlServerCdcInsertAsync(
                    connectionString,
                    committed.Fingerprint.EventId,
                    TimeSpan.FromSeconds(60)))
            {
                Assert.Inconclusive(
                    "SQL Server CDC change table did not observe insert within timeout.");
            }

            var consumed = await CdcDebeziumE2ESupport.TryConsumeShadowEventAsync(
                pipeline,
                topic,
                committed.Fingerprint.EventId,
                TimeSpan.FromSeconds(120));
            if (consumed is null)
            {
                Assert.Inconclusive(
                    "Debezium did not publish the outbox event to Kafka within timeout.");
            }

            var status = await CdcDebeziumE2ESupport.ConsumeThroughInboxAsync(
                options,
                consumed);
            Assert.AreEqual(InboxConsumeStatus.Processed, status);
        }
        finally
        {
            await connectAdmin.DeleteConnectorAsync(connectorName);
        }
    }
}
