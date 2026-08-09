using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Messaging.Features.ChangeDeliveryOwner;
using Full.NET.Modules.Messaging.Features.RollbackDeliveryOwner;
using Full.NET.Modules.Messaging.Persistence;
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

    [TestMethod]
    public async Task SqlServer_rollback_fails_closed_without_verified_control_plane_readiness()
    {
        await VerifyRollbackFailsClosedAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_rollback_fails_closed_without_verified_control_plane_readiness()
    {
        await VerifyRollbackFailsClosedAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task SqlServer_rollback_fence_drains_in_flight_producer_and_rejects_new_writes()
    {
        await VerifyRollbackProducerFenceAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_rollback_fence_drains_in_flight_producer_and_rejects_new_writes()
    {
        await VerifyRollbackProducerFenceAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task SqlServer_abort_failure_keeps_persisted_producer_fence()
    {
        await VerifyAbortFailureKeepsProducerFenceAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_abort_failure_keeps_persisted_producer_fence()
    {
        await VerifyAbortFailureKeepsProducerFenceAsync(
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
        var rollbackReadiness = new MutableRollbackReadinessReader();
        await using var serviceProvider = await EventDeliveryPilotTestSupport
            .BuildPilotServicesAsync(
                options,
                rollbackReadinessReader: rollbackReadiness);
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
        await using (var readinessScope = EventDeliveryPilotTestSupport
                         .CreateHostScope(serviceProvider))
        {
            var ownershipStore = readinessScope.ServiceProvider
                .GetRequiredService<EventStreamOwnershipStore>();
            var lastPublished = await ownershipStore
                .FindLastAppendOnlyOutboxEventAsync(
                    EventDeliveryPilotTestSupport.PilotEventType,
                    EventDeliveryPilotTestSupport.PilotSchemaVersion,
                    CancellationToken.None);
            Assert.IsNotNull(lastPublished);
            rollbackReadiness.MarkReady(lastPublished!.CutoffEventId);
        }
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

    private static async Task VerifyRollbackFailsClosedAsync(
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

        await EventDeliveryPilotTestSupport.WritePilotOutboxEventAsync(
            serviceProvider,
            CancellationToken.None);
        await EventDeliveryPilotTestSupport.ProcessOutboxOnceAsync(
            serviceProvider,
            CancellationToken.None);

        await using var scope = EventDeliveryPilotTestSupport
            .CreateHostScope(serviceProvider);
        var cutover = scope.ServiceProvider.GetRequiredService<DeliveryCutoverService>();
        var cutoverResult = await cutover.CutoverAsync(
            new ChangeDeliveryOwnerRequest(
                EventDeliveryPilotTestSupport.PilotEventType,
                EventDeliveryPilotTestSupport.PilotSchemaVersion,
                EventDeliveryOwner.CdcKafka,
                "pilot-cutover"),
            CancellationToken.None);
        Assert.IsTrue(cutoverResult.IsSuccess);

        var rollback = scope.ServiceProvider.GetRequiredService<DeliveryRollbackService>();
        var rollbackResult = await rollback.RollbackAsync(
            new ChangeDeliveryOwnerRequest(
                EventDeliveryPilotTestSupport.PilotEventType,
                EventDeliveryPilotTestSupport.PilotSchemaVersion,
                EventDeliveryOwner.LegacyPolling,
                "unsafe-rollback"),
            CancellationToken.None);

        Assert.IsFalse(rollbackResult.IsSuccess);
        Assert.AreEqual(
            MessagingErrorCodes.RollbackPreconditionFailed,
            rollbackResult.Error?.Code);

        // 控制面准备失败必须撤销同一 generation 的数据库 fence，避免流永久停止写入。
        await EventDeliveryPilotTestSupport.WritePilotOutboxEventAsync(
            serviceProvider,
            CancellationToken.None);
    }

    private static async Task VerifyRollbackProducerFenceAsync(
        DatabaseProvider provider,
        string connectionString)
    {
        var options = new DatabaseOptions
        {
            Provider = provider,
            ConnectionString = connectionString,
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
        };
        var readiness = new CoordinatedRollbackReadinessReader();
        await using var serviceProvider = await EventDeliveryPilotTestSupport
            .BuildPilotServicesAsync(options, rollbackReadinessReader: readiness);

        await EventDeliveryPilotTestSupport.WritePilotOutboxEventAsync(
            serviceProvider,
            CancellationToken.None);
        await EventDeliveryPilotTestSupport.ProcessOutboxOnceAsync(
            serviceProvider,
            CancellationToken.None);
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

        var producerInserted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProducer = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlightProducer = EventDeliveryPilotTestSupport
            .WritePilotOutboxEventHoldingTransactionAsync(
                serviceProvider,
                producerInserted,
                releaseProducer.Task,
                CancellationToken.None);
        await producerInserted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Task<Result<DeliveryRollbackResponse>> rollbackTask;
        await using (var scope = EventDeliveryPilotTestSupport.CreateHostScope(serviceProvider))
        {
            var rollback = scope.ServiceProvider.GetRequiredService<DeliveryRollbackService>();
            rollbackTask = rollback.RollbackAsync(
                new ChangeDeliveryOwnerRequest(
                    EventDeliveryPilotTestSupport.PilotEventType,
                    EventDeliveryPilotTestSupport.PilotSchemaVersion,
                    EventDeliveryOwner.LegacyPolling,
                    "producer-fence-test"),
                CancellationToken.None);

            await Task.Delay(250);
            Assert.IsFalse(
                readiness.PrepareStarted.Task.IsCompleted,
                "控制面准备不得越过尚未提交的共享生产者事务。");
            releaseProducer.TrySetResult();
            await inFlightProducer.WaitAsync(TimeSpan.FromSeconds(10));
            await readiness.PrepareStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

            await Assert.ThrowsExactlyAsync<EventDeliveryProducerFencedException>(
                () => EventDeliveryPilotTestSupport.WritePilotOutboxEventAsync(
                    serviceProvider,
                    CancellationToken.None));

            readiness.ReleasePrepare.TrySetResult();
            var rollbackResult = await rollbackTask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsTrue(rollbackResult.IsSuccess);
        }
    }

    private static async Task VerifyAbortFailureKeepsProducerFenceAsync(
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
            .BuildPilotServicesAsync(
                options,
                rollbackReadinessReader: new ThrowingAbortRollbackReadinessReader());

        await EventDeliveryPilotTestSupport.WritePilotOutboxEventAsync(
            serviceProvider,
            CancellationToken.None);
        await EventDeliveryPilotTestSupport.ProcessOutboxOnceAsync(
            serviceProvider,
            CancellationToken.None);
        await using var scope = EventDeliveryPilotTestSupport.CreateHostScope(serviceProvider);
        var cutover = scope.ServiceProvider.GetRequiredService<DeliveryCutoverService>();
        var cutoverResult = await cutover.CutoverAsync(
            new ChangeDeliveryOwnerRequest(
                EventDeliveryPilotTestSupport.PilotEventType,
                EventDeliveryPilotTestSupport.PilotSchemaVersion,
                EventDeliveryOwner.CdcKafka,
                "pilot-cutover"),
            CancellationToken.None);
        Assert.IsTrue(cutoverResult.IsSuccess);

        var rollback = scope.ServiceProvider.GetRequiredService<DeliveryRollbackService>();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => rollback.RollbackAsync(
                new ChangeDeliveryOwnerRequest(
                    EventDeliveryPilotTestSupport.PilotEventType,
                    EventDeliveryPilotTestSupport.PilotSchemaVersion,
                    EventDeliveryOwner.LegacyPolling,
                    "abort-failure-test"),
                CancellationToken.None));

        await Assert.ThrowsExactlyAsync<EventDeliveryProducerFencedException>(
            () => EventDeliveryPilotTestSupport.WritePilotOutboxEventAsync(
                serviceProvider,
                CancellationToken.None));
    }

    private sealed class MutableRollbackReadinessReader
        : IEventDeliveryRollbackReadinessReader
    {
        private EventDeliveryRollbackReadiness _readiness =
            EventDeliveryRollbackReadiness.Unavailable;

        public Task<EventDeliveryRollbackReadiness> PrepareAsync(
            string eventType,
            int schemaVersion,
            Guid rollbackGeneration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_readiness with { RollbackGeneration = rollbackGeneration });

        public Task AbortAsync(
            string eventType,
            int schemaVersion,
            Guid rollbackGeneration,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void MarkReady(Guid lastPublishedEventId)
        {
            _readiness = new EventDeliveryRollbackReadiness(
                RollbackGeneration: Guid.Empty,
                ConnectorStopped: true,
                BrokerMessagesDrainedOrIsolated: true,
                SourcePositionCoversProducerFence: true,
                ProducerFencePositionJson: "{\"position\":\"database-fence\"}",
                CdcSourcePositionJson: "{\"position\":\"test-safe-boundary\"}",
                ControlPlaneFenceToken: "test-fence-token",
                LastPublishedEventId: lastPublishedEventId,
                ObservedAtUtc: DateTimeOffset.UtcNow);
        }
    }

    private sealed class CoordinatedRollbackReadinessReader
        : IEventDeliveryRollbackReadinessReader
    {
        public TaskCompletionSource PrepareStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleasePrepare { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<EventDeliveryRollbackReadiness> PrepareAsync(
            string eventType,
            int schemaVersion,
            Guid rollbackGeneration,
            CancellationToken cancellationToken = default)
        {
            PrepareStarted.TrySetResult();
            await ReleasePrepare.Task.WaitAsync(cancellationToken);
            return new EventDeliveryRollbackReadiness(
                rollbackGeneration,
                ConnectorStopped: true,
                BrokerMessagesDrainedOrIsolated: true,
                SourcePositionCoversProducerFence: true,
                ProducerFencePositionJson: "{\"position\":\"database-fence\"}",
                CdcSourcePositionJson: "{\"position\":\"connector-fence\"}",
                ControlPlaneFenceToken: "coordinated-fence",
                LastPublishedEventId: Guid.CreateVersion7(),
                ObservedAtUtc: DateTimeOffset.UtcNow);
        }

        public Task AbortAsync(
            string eventType,
            int schemaVersion,
            Guid rollbackGeneration,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ThrowingAbortRollbackReadinessReader
        : IEventDeliveryRollbackReadinessReader
    {
        public Task<EventDeliveryRollbackReadiness> PrepareAsync(
            string eventType,
            int schemaVersion,
            Guid rollbackGeneration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EventDeliveryRollbackReadiness(
                rollbackGeneration,
                ConnectorStopped: true,
                BrokerMessagesDrainedOrIsolated: false,
                SourcePositionCoversProducerFence: false,
                ProducerFencePositionJson: "{\"position\":\"database-fence\"}",
                CdcSourcePositionJson: null,
                ControlPlaneFenceToken: "failed-control-plane-fence",
                LastPublishedEventId: null,
                ObservedAtUtc: DateTimeOffset.UtcNow));

        public Task AbortAsync(
            string eventType,
            int schemaVersion,
            Guid rollbackGeneration,
            CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException(
                "Simulated control-plane recovery failure."));
    }
}
