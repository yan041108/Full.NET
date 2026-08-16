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
    public async Task MySql_duplicate_kafka_delivery_is_idempotent_for_inbox_and_projection()
    {
        await using var scenario = await RequireMySqlScenarioAsync();
        await OrganizationUnitCdcKafkaEndToEndSupport.SeedCdcKafkaStreamOwnershipAsync(
            scenario.Options);
        await using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            scenario.ConnectionString);
        var (tenantId, unitId, unitName) =
            await OrganizationUnitCdcKafkaEndToEndSupport.CreateOrganizationUnitViaApiAsync(
                factory,
                CancellationToken.None);

        var connectorName = $"fullnet-org-fault-{Guid.NewGuid():N}";
        var connectorConfig = await scenario.CreateConnectorConfigAsync();
        try
        {
            await CdcDebeziumConnectorTestSupport.RegisterHealthyShadowConnectorAsync(
                scenario.ConnectAdmin,
                connectorName,
                connectorConfig,
                TimeSpan.FromSeconds(120));

            var eventId = await OrganizationUnitCdcKafkaEndToEndSupport
                .ReadLatestOrganizationOutboxEventIdAsync(scenario.Options);
            var topic = CdcDebeziumE2ESupport.GetShadowTopic(
                EventDeliveryPilotTestSupport.PilotEventType);
            var consumed = await CdcDebeziumE2ESupport.TryConsumeShadowEventAsync(
                scenario.Pipeline,
                topic,
                eventId,
                TimeSpan.FromSeconds(120));
            if (consumed is null)
            {
                Assert.Inconclusive(
                    "Debezium did not publish organization unit changed event within timeout.");
            }

            var first = await OrganizationCdcKafkaIdentityProjectionE2ESupport
                .ConsumeOrganizationEventThroughInboxAsync(scenario.Options, consumed);
            var second = await OrganizationCdcKafkaIdentityProjectionE2ESupport
                .ConsumeOrganizationEventThroughInboxAsync(scenario.Options, consumed);
            Assert.AreEqual(InboxConsumeStatus.Processed, first);
            Assert.AreEqual(InboxConsumeStatus.Processed, second);

            var projected = await OrganizationUnitCdcKafkaEndToEndSupport.ProjectionExistsAsync(
                scenario.Options,
                tenantId,
                unitId,
                unitName);
            Assert.IsTrue(projected);
        }
        finally
        {
            await scenario.ConnectAdmin.DeleteConnectorAsync(connectorName);
        }
    }

    private static async Task<CdcDebeziumMySqlE2EScenario> RequireMySqlScenarioAsync()
    {
        var scenario = await CdcDebeziumMySqlE2EScenario.TryCreateAsync();
        if (scenario is null)
        {
            Assert.Inconclusive(
                "MySQL CDC/Debezium prerequisites are unavailable in this environment.");
        }

        return scenario;
    }
}
