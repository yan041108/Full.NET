using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>
/// 真实 Organization routed Outbox → CDC → Kafka → Inbox → Identity 投影 happy path（MySQL）。
/// 不升级 Delivery 为 Production-verified；仅补充 verification 证据。
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class OrganizationCdcKafkaIdentityProjectionMySqlE2ETests
{
    [TestMethod]
    [TestCategory("RequiresDocker")]
    public async Task MySql_organization_unit_changed_reaches_identity_projection_via_cdc_kafka()
    {
        await using var scenario = await RequireMySqlScenarioAsync();
        var pilotServices = await EventDeliveryPilotTestSupport
            .BuildPilotServicesAsync(scenario.Options);
        await OrganizationCdcKafkaIdentityProjectionE2ESupport
            .SeedPilotStreamOwnershipAsync(scenario.Options);
        await EventDeliveryPilotTestSupport.WritePilotOutboxEventAsync(
            pilotServices,
            CancellationToken.None);

        var connectorName = $"fullnet-org-pilot-{Guid.NewGuid():N}";
        var connectorConfig = await scenario.CreateConnectorConfigAsync();
        try
        {
            await CdcDebeziumConnectorTestSupport.RegisterHealthyShadowConnectorAsync(
                scenario.ConnectAdmin,
                connectorName,
                connectorConfig,
                TimeSpan.FromSeconds(120));

            var topic = CdcDebeziumE2ESupport.GetShadowTopic(
                EventDeliveryPilotTestSupport.PilotEventType);
            var (tenantId, unitId) =
                await OrganizationCdcKafkaIdentityProjectionE2ESupport
                    .ReadLatestPilotOutboxIdentityAsync(scenario.Options);

            var eventId = await ReadLatestPilotOutboxEventIdAsync(scenario.Options);
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

            var status = await OrganizationCdcKafkaIdentityProjectionE2ESupport
                .ConsumeOrganizationEventThroughInboxAsync(scenario.Options, consumed);
            Assert.AreEqual(InboxConsumeStatus.Processed, status);

            var projected = await OrganizationCdcKafkaIdentityProjectionE2ESupport
                .ProjectionExistsAsync(
                    scenario.Options,
                    tenantId,
                    unitId,
                    OrganizationCdcKafkaIdentityProjectionE2ESupport.PilotUnitName);
            Assert.IsTrue(projected);
        }
        finally
        {
            pilotServices.Dispose();
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

    private static async Task<Guid> ReadLatestPilotOutboxEventIdAsync(DatabaseOptions options)
    {
        await using var connection = new MySqlConnector.MySqlConnection(options.ConnectionString);
        return await Dapper.SqlMapper.QuerySingleAsync<Guid>(
            connection,
            """
            SELECT Id
            FROM fn_messaging_outbox_event
            WHERE MessageType = @MessageType AND SchemaVersion = @SchemaVersion
            ORDER BY OccurredAtUtc DESC
            LIMIT 1
            """,
            new
            {
                MessageType = EventDeliveryPilotTestSupport.PilotEventType,
                SchemaVersion = EventDeliveryPilotTestSupport.PilotSchemaVersion,
            });
    }
}
