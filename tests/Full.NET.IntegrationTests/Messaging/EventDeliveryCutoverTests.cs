using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging.Contracts;

using Full.NET.Modules.Messaging.Features.ChangeDeliveryOwner;

using Full.NET.Modules.Messaging.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Messaging;

[TestClass]
public sealed class EventDeliveryCutoverTests
{
    [TestMethod]
    public async Task SqlServer_pilot_stream_cutover_persists_ownership_and_revokes_legacy_worker()
    {
        await VerifyPilotCutoverAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_pilot_stream_cutover_persists_ownership_and_revokes_legacy_worker()
    {
        await VerifyPilotCutoverAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    private static async Task VerifyPilotCutoverAsync(
        DatabaseProvider provider,
        string connectionString)
    {
        var options = new DatabaseOptions
        {
            Provider = provider,
            ConnectionString = connectionString,
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
        };
        await using var serviceProvider = await EventDeliveryPilotTestSupport
            .BuildPilotServicesAsync(options);
        var handler = (EventDeliveryPilotTestSupport.PilotEventRecordingHandler)serviceProvider
            .GetRequiredService<IIntegrationEventHandler>();
        await EventDeliveryPilotTestSupport.WritePilotOutboxEventAsync(
            serviceProvider,
            CancellationToken.None);
        await EventDeliveryPilotTestSupport.ProcessOutboxOnceAsync(
            serviceProvider,
            CancellationToken.None);
        Assert.AreEqual(1, handler.HandledCount);

        OutboxStreamCutoffRecord? expectedCutoff;
        await using (var scope = EventDeliveryPilotTestSupport.CreateHostScope(serviceProvider))
        {
            expectedCutoff = await scope.ServiceProvider
                .GetRequiredService<EventStreamOwnershipStore>()
                .FindLastOutboxEventAsync(
                    EventDeliveryPilotTestSupport.PilotEventType,
                    EventDeliveryPilotTestSupport.PilotSchemaVersion,
                    CancellationToken.None);
        }
        Assert.IsNotNull(expectedCutoff);

        await using (var scope = EventDeliveryPilotTestSupport.CreateHostScope(serviceProvider))
        {
            var cutover = scope.ServiceProvider.GetRequiredService<DeliveryCutoverService>();
            var result = await cutover.CutoverAsync(
                new ChangeDeliveryOwnerRequest(
                    EventDeliveryPilotTestSupport.PilotEventType,
                    EventDeliveryPilotTestSupport.PilotSchemaVersion,
                    EventDeliveryOwner.CdcKafka,
                    "pilot-cutover"),
                CancellationToken.None);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(result.Value!.OwnershipPersisted);
            Assert.AreEqual(expectedCutoff!.CutoffEventId, result.Value.CutoffEventId);
            Assert.AreEqual(expectedCutoff.CutoffOccurredAtUtc, result.Value.CutoffOccurredAtUtc);
            Assert.AreEqual(EventDeliveryOwner.LegacyPolling, result.Value.CurrentOwner);
            Assert.AreEqual(EventDeliveryOwner.CdcKafka, result.Value.TargetOwner);
        }

        EventStreamOwnershipRecord? ownership;
        await using (var scope = EventDeliveryPilotTestSupport.CreateHostScope(serviceProvider))
        {
            var ownershipStore = scope.ServiceProvider
                .GetRequiredService<IEventStreamOwnershipStore>();
            ownership = await ownershipStore.FindAsync(
                EventDeliveryPilotTestSupport.PilotEventType,
                EventDeliveryPilotTestSupport.PilotSchemaVersion,
                CancellationToken.None);
        }
        Assert.IsNotNull(ownership);
        Assert.AreEqual(EventDeliveryOwner.CdcKafka, ownership!.CurrentOwner);

        await EventDeliveryPilotTestSupport.WritePilotOutboxEventAsync(
            serviceProvider,
            CancellationToken.None);
        await EventDeliveryPilotTestSupport.ProcessOutboxOnceAsync(
            serviceProvider,
            CancellationToken.None);
        Assert.AreEqual(1, handler.HandledCount);
    }
}
