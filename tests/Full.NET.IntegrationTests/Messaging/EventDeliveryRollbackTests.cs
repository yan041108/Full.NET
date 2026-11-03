using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Results;
using Dapper;
using System.Data.Common;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging.Contracts;
using Full.NET.Modules.Messaging.Features.ChangeDeliveryOwner;
using Full.NET.Modules.Messaging.Features.RollbackDeliveryOwner;
using Full.NET.Modules.Messaging.Persistence;
using Full.NET.Modules.Messaging.Auditing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>集成测试：EventDeliveryRollback。</summary>
[TestClass, DoNotParallelize]
public sealed class EventDeliveryRollbackTests
{
    [TestMethod]
    public async Task SqlServer_rollback_commit_fails_after_control_session_loss() =>
        await VerifyControlSessionLossAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(), false);

    [TestMethod]
    public async Task MySql_rollback_commit_fails_after_control_session_loss() =>
        await VerifyControlSessionLossAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), false);

    [TestMethod]
    public async Task SqlServer_recovery_commit_fails_after_control_session_loss() =>
        await VerifyControlSessionLossAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(), true, true);

    [TestMethod]
    public async Task MySql_recovery_commit_fails_after_control_session_loss() =>
        await VerifyControlSessionLossAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), true, true);

    [TestMethod]
    public async Task SqlServer_recovery_prepare_fails_after_control_session_loss() =>
        await VerifyControlSessionLossAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(), true);

    [TestMethod]
    public async Task MySql_recovery_prepare_fails_after_control_session_loss() =>
        await VerifyControlSessionLossAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), true);

    private static async Task VerifyControlSessionLossAsync(DatabaseProvider provider, string connectionString, bool recovery, bool completion = false)
    {
        var gate = new RollbackAuditGate();
        var readiness = new MutableRollbackReadinessReader();
        readiness.MarkReady(Guid.CreateVersion7());
        var options = new DatabaseOptions { Provider = provider, ConnectionString = connectionString, MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16 };
        await using var services = await EventDeliveryPilotTestSupport.BuildPilotServicesAsync(options, rollbackReadinessReader: readiness,
            configureServices: registrations =>
            {
                registrations.RemoveAll<ITransactionalDomainAuditWriter<MessagingDomainAuditWrite>>();
                registrations.AddScoped<MessagingDomainAuditWriter>();
                registrations.AddScoped<ITransactionalDomainAuditWriter<MessagingDomainAuditWrite>>(scope =>
                    new GatedRollbackAudit(scope.GetRequiredService<MessagingDomainAuditWriter>(), gate));
            });
        await using (var setup = EventDeliveryPilotTestSupport.CreateHostScope(services))
        {
            var cutover = await setup.ServiceProvider.GetRequiredService<DeliveryCutoverService>().CutoverAsync(new(
                EventDeliveryPilotTestSupport.PilotEventType, 1, EventDeliveryOwner.CdcKafka, "session loss setup"));
            Assert.IsTrue(cutover.IsSuccess, cutover.Error?.Message);
        }
        await using var scope = EventDeliveryPilotTestSupport.CreateHostScope(services);
        var generation = Guid.CreateVersion7();
        if (recovery)
        {
            var store = scope.ServiceProvider.GetRequiredService<EventStreamOwnershipStore>();
            var journal = scope.ServiceProvider.GetRequiredService<DeliveryRollbackJournal>();
            await scope.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync(async token =>
            {
                Assert.IsTrue(await store.TryBeginRollbackPreparationAsync(EventDeliveryPilotTestSupport.PilotEventType, 1, generation, DateTimeOffset.UtcNow, token));
                Assert.AreEqual(1, await journal.CreateAsync(generation, EventDeliveryPilotTestSupport.PilotEventType, 1, Guid.CreateVersion7(), "recovery setup", token));
                return 0;
            }, CancellationToken.None);
        }
        gate.Action = recovery ? MessagingDomainAuditActionKeys.DeliveryRollbackRecovery : MessagingDomainAuditActionKeys.DeliveryRollback;
        gate.Outcome = recovery && !completion ? MessagingDomainAuditOutcomes.Requested : MessagingDomainAuditOutcomes.Success;
        gate.Armed = 1;
        Task operation = recovery
            ? scope.ServiceProvider.GetRequiredService<DeliveryRollbackRecoveryService>().RecoverAsync(generation, new("recovery loss"), CancellationToken.None)
            : scope.ServiceProvider.GetRequiredService<DeliveryRollbackService>().RollbackAsync(new(EventDeliveryPilotTestSupport.PilotEventType, 1, EventDeliveryOwner.LegacyPolling, "rollback loss"));
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromMinutes(2));
            await using DbConnection db = provider == DatabaseProvider.SqlServer ? new Microsoft.Data.SqlClient.SqlConnection(connectionString) : new MySqlConnector.MySqlConnection(connectionString);
            await db.OpenAsync();
            var id = provider == DatabaseProvider.SqlServer
                ? await db.QuerySingleAsync<int>("SELECT request_session_id FROM sys.dm_tran_locks WHERE resource_type='APPLICATION' AND request_owner_type='SESSION' AND resource_database_id=DB_ID() AND resource_description LIKE '%messaging.delivery_control%'")
                : await db.QuerySingleAsync<int>("SELECT IS_USED_LOCK('messaging.delivery_control')");
            Assert.IsGreaterThan(0, id);
            await db.ExecuteAsync("KILL " + id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            gate.Release.TrySetResult();
            Exception? error = null;
            try { await operation; } catch (Exception exception) { error = exception; }
            Assert.IsNotNull(error, "控制连接失效后，写入中的事务不得提交。");
            if (!recovery) generation = await db.QuerySingleAsync<Guid>("SELECT RollbackGeneration FROM fn_messaging_stream_ownership WHERE MessageType=@type", new { type = EventDeliveryPilotTestSupport.PilotEventType });
            Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT CurrentOwner FROM fn_messaging_stream_ownership WHERE MessageType=@type", new { type = EventDeliveryPilotTestSupport.PilotEventType }));
            Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT RollbackState FROM fn_messaging_stream_ownership WHERE MessageType=@type", new { type = EventDeliveryPilotTestSupport.PilotEventType }));
            await using var fresh = EventDeliveryPilotTestSupport.CreateHostScope(services);
            var recovered = await fresh.ServiceProvider.GetRequiredService<DeliveryRollbackRecoveryService>().RecoverAsync(generation, new("fresh recovery"), CancellationToken.None);
            Assert.IsTrue(recovered.IsSuccess, recovered.Error?.Message);
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT RollbackState FROM fn_messaging_stream_ownership WHERE MessageType=@type", new { type = EventDeliveryPilotTestSupport.PilotEventType }));
        }
        finally { gate.Release.TrySetResult(); }
    }

    private sealed class RollbackAuditGate
    {
        internal string Action = "";
        internal string Outcome = "";
        internal int Armed;
        internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class GatedRollbackAudit(MessagingDomainAuditWriter inner, RollbackAuditGate gate) : ITransactionalDomainAuditWriter<MessagingDomainAuditWrite>
    {
        public async Task WriteAsync(MessagingDomainAuditWrite value, CancellationToken cancellationToken)
        {
            await inner.WriteAsync(value, cancellationToken);
            if (value.ActionKey == gate.Action && value.Outcome == gate.Outcome && Interlocked.Exchange(ref gate.Armed, 0) == 1)
            {
                gate.Reached.TrySetResult();
                await gate.Release.Task.WaitAsync(cancellationToken);
            }
        }
    }
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
            EventDeliveryControlLease lease, CancellationToken cancellationToken = default) =>
            Task.FromResult(_readiness with { RollbackGeneration = rollbackGeneration });

        public Task AbortAsync(
            string eventType,
            int schemaVersion,
            Guid rollbackGeneration,
            EventDeliveryControlLease lease, CancellationToken cancellationToken = default) =>
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
            EventDeliveryControlLease lease, CancellationToken cancellationToken = default)
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
            EventDeliveryControlLease lease, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ThrowingAbortRollbackReadinessReader
        : IEventDeliveryRollbackReadinessReader
    {
        public Task<EventDeliveryRollbackReadiness> PrepareAsync(
            string eventType,
            int schemaVersion,
            Guid rollbackGeneration,
            EventDeliveryControlLease lease, CancellationToken cancellationToken = default) =>
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
            EventDeliveryControlLease lease, CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException(
                "Simulated control-plane recovery failure."));
    }
}
