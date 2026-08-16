using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Messaging.Abstractions;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>
/// Organization CDC 试点故障矩阵（Task 6 Step 2 子集）。
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class OrganizationUnitCdcKafkaFaultMatrixTests
{
    [TestMethod]
    [TestCategory("RequiresDocker")]
    [DataRow(DatabaseProvider.MySql)]
    public async Task MySql_duplicate_kafka_delivery_is_idempotent_for_inbox_and_projection(
        DatabaseProvider provider)
    {
        await RunDuplicateDeliveryFaultMatrixAsync(provider);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    public async Task SqlServer_duplicate_kafka_delivery_is_idempotent_for_inbox_and_projection(
        DatabaseProvider provider)
    {
        await RunDuplicateDeliveryFaultMatrixAsync(provider);
    }

    private static async Task RunDuplicateDeliveryFaultMatrixAsync(DatabaseProvider provider)
    {
        var pipeline = await CdcDebeziumPipelineFixture.GetOrStartAsync();
        var connectionString = provider == DatabaseProvider.MySql
            ? await SharedDatabaseFixture.CreateMySqlDatabaseAsync()
            : await SqlServerCdcTestSupport.ResolveConnectionStringAsync();
        var options = new DatabaseOptions
        {
            Provider = provider,
            ConnectionString = connectionString,
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
            CommandTimeoutSeconds = 300,
        };
        await MessagingOutboxTestSupport.MigrateAsync(options);

        if (provider == DatabaseProvider.MySql)
        {
            var binlogStatus = await CdcShadowFixture.ReadMySqlBinlogStatusAsync(connectionString);
            if (!binlogStatus.IsRowFullEnabled)
            {
                Assert.Inconclusive("MySQL binlog ROW/FULL is unavailable.");
            }
        }
        else
        {
            var cdcEnablement = await SqlServerCdcTestSupport.TryEnableCdcAsync(connectionString);
            if (!cdcEnablement.Succeeded)
            {
                Assert.Inconclusive(SqlServerCdcTestSupport.BuildInconclusiveMessage(cdcEnablement));
            }
        }

        await OrganizationUnitCdcKafkaEndToEndSupport.SeedCdcKafkaStreamOwnershipAsync(options);

        using var connectAdmin = pipeline.CreateConnectAdminClient();
        if (!await connectAdmin.WaitUntilReadyAsync(TimeSpan.FromSeconds(60)))
        {
            Assert.Inconclusive("Debezium Connect did not become ready within timeout.");
        }

        await using var factory = new FullNetApiFactory(provider, connectionString);
        var connectorName = $"fullnet-org-fault-{Guid.NewGuid():N}";
        var connectorConfig = provider == DatabaseProvider.MySql
            ? await DebeziumConnectorTemplateFactory.CreateMySqlShadowConfigAsync(
                connectionString,
                pipeline.HostGateway,
                pipeline.InternalKafkaBootstrapServers)
            : await DebeziumConnectorTemplateFactory.CreateSqlServerShadowConfigAsync(
                connectionString,
                pipeline.HostGateway,
                pipeline.InternalKafkaBootstrapServers);
        try
        {
            await CdcDebeziumConnectorTestSupport.RegisterHealthyShadowConnectorAsync(
                connectAdmin,
                connectorName,
                connectorConfig,
                TimeSpan.FromSeconds(240));

            var (tenantId, unitId, unitName) =
                await OrganizationUnitCdcKafkaEndToEndSupport.CreateOrganizationUnitViaApiAsync(
                    factory,
                    CancellationToken.None);

            var eventId = await OrganizationUnitCdcKafkaEndToEndSupport
                .ReadLatestOrganizationOutboxEventIdAsync(options);
            var topic = CdcDebeziumE2ESupport.GetShadowTopic(
                EventDeliveryPilotTestSupport.PilotEventType);

            if (provider == DatabaseProvider.SqlServer
                && !await CdcShadowFixture.WaitForSqlServerCdcInsertAsync(
                    connectionString,
                    eventId,
                    TimeSpan.FromSeconds(180)))
            {
                Assert.Inconclusive(
                    "SQL Server CDC change table did not observe organization outbox insert.");
            }

            var consumed = await CdcDebeziumE2ESupport.TryConsumeShadowEventAsync(
                pipeline,
                topic,
                eventId,
                TimeSpan.FromSeconds(240));
            if (consumed is null)
            {
                Assert.Inconclusive(
                    "Debezium did not publish organization unit changed event within timeout.");
            }

            var first = await OrganizationCdcKafkaIdentityProjectionE2ESupport
                .ConsumeOrganizationEventThroughInboxAsync(options, consumed);
            var second = await OrganizationCdcKafkaIdentityProjectionE2ESupport
                .ConsumeOrganizationEventThroughInboxAsync(options, consumed);
            Assert.AreEqual(InboxConsumeStatus.Processed, first);
            Assert.AreEqual(InboxConsumeStatus.AlreadyProcessed, second);

            var projected = await OrganizationUnitCdcKafkaEndToEndSupport.ProjectionExistsAsync(
                options,
                tenantId,
                unitId,
                unitName);
            Assert.IsTrue(projected);
        }
        finally
        {
            await connectAdmin.DeleteConnectorAsync(connectorName);
        }
    }
}
