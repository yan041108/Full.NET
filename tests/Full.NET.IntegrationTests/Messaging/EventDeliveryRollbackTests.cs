using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Messaging.Features.ChangeDeliveryOwner;
using Full.NET.Modules.Messaging.Features.RollbackDeliveryOwner;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Messaging;

[TestClass]
public sealed class EventDeliveryRollbackTests
{
    [TestMethod]
    public async Task SqlServer_pilot_stream_rollback_restores_legacy_worker()
    {
        await VerifyPilotRollbackAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_pilot_stream_rollback_restores_legacy_worker()
    {
        await VerifyPilotRollbackAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    private static async Task VerifyPilotRollbackAsync(
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

        await using (var scope = EventDeliveryPilotTestSupport.CreateHostScope(serviceProvider))
        {
            var cutover = scope.ServiceProvider.GetRequiredService<DeliveryCutoverService>();
            var cutoverResult = await cutover.CutoverAsync(
                new ChangeDeliveryOwnerRequest(
                    EventDeliveryPilotTestSupport.PilotEventType,
                    EventDeliveryPilotTestSupport.PilotSchemaVersion,
                    EventDeliveryOwner.CdcKafka,
                    "pilot-cutover"),
                CancellationToken.None);
            Assert.IsTrue(cutoverResult.IsSuccess);
        }

        await EventDeliveryPilotTestSupport.WritePilotOutboxEventAsync(
            serviceProvider,
            CancellationToken.None);
        await EventDeliveryPilotTestSupport.ProcessOutboxOnceAsync(
            serviceProvider,
            CancellationToken.None);
        Assert.AreEqual(1, handler.HandledCount);

        await using (var scope = EventDeliveryPilotTestSupport.CreateHostScope(serviceProvider))
        {
            var rollback = scope.ServiceProvider.GetRequiredService<DeliveryRollbackService>();
            var rollbackResult = await rollback.RollbackAsync(
                new ChangeDeliveryOwnerRequest(
                    EventDeliveryPilotTestSupport.PilotEventType,
                    EventDeliveryPilotTestSupport.PilotSchemaVersion,
                    EventDeliveryOwner.LegacyPolling,
                    "pilot-rollback"),
                CancellationToken.None);
            Assert.IsTrue(rollbackResult.IsSuccess);
            Assert.IsTrue(rollbackResult.Value!.OwnershipPersisted);
            Assert.AreEqual(EventDeliveryOwner.CdcKafka, rollbackResult.Value.CurrentOwner);
            Assert.AreEqual(EventDeliveryOwner.LegacyPolling, rollbackResult.Value.TargetOwner);
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
        Assert.AreEqual(EventDeliveryOwner.LegacyPolling, ownership!.CurrentOwner);
        Assert.IsNotNull(ownership.RollbackOccurredAtUtc);

        await EventDeliveryPilotTestSupport.WritePilotOutboxEventAsync(
            serviceProvider,
            CancellationToken.None);
        await EventDeliveryPilotTestSupport.ProcessOutboxOnceAsync(
            serviceProvider,
            CancellationToken.None);
        Assert.AreEqual(2, handler.HandledCount);
    }
}
