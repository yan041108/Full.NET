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

    [TestMethod]
    public async Task SqlServer_cutover_succeeds_when_target_stream_empty_but_other_legacy_stream_has_backlog()
    {
        // RED 期望：当前 DeliveryCutoverService 使用全局 ReadBacklogAsync，
        // 当其他 Legacy 流有积压时，即使目标流已清空也会错误返回 LegacyBacklogNotDrained。
        // GREEN 期望：只检查目标流 (EventType, SchemaVersion) 的积压，允许切流。
        await VerifyCutoverIsStreamScopedNotGlobalAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_cutover_succeeds_when_target_stream_empty_but_other_legacy_stream_has_backlog()
    {
        await VerifyCutoverIsStreamScopedNotGlobalAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    [TestMethod]
    public async Task SqlServer_cutover_fails_when_target_stream_has_pending_retry_active_lease()
    {
        await VerifyCutoverFailsForTargetStreamBacklogAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_cutover_fails_when_target_stream_has_pending_retry_active_lease()
    {
        await VerifyCutoverFailsForTargetStreamBacklogAsync(
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

    private static async Task VerifyCutoverIsStreamScopedNotGlobalAsync(
        DatabaseProvider provider,
        string connectionString)
    {
        // 场景：
        // 1) 先写并处理 Pilot 目标流 → 目标流清空
        // 2) 再插入一条"无关 Legacy 流"的 pending 消息（不会被 Handler 匹配，因此保持 pending）
        // 3) 对 Pilot 流请求切流 → 应该成功（只检查目标流）
        // 4) RED 阶段当前会失败，因为 ReadBacklogAsync 是全局的，看到其他流有 pending 就阻止切流。
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

        // 步骤 1：处理并清空目标 Pilot 流
        await EventDeliveryPilotTestSupport.WritePilotOutboxEventAsync(
            serviceProvider,
            CancellationToken.None);
        await EventDeliveryPilotTestSupport.ProcessOutboxOnceAsync(
            serviceProvider,
            CancellationToken.None);
        Assert.AreEqual(1, handler.HandledCount);

        // 步骤 2：插入无关 Legacy 流的 pending 消息（MessageType 与 PilotEventType 不同）
        const string otherLegacyEventType = "fullnet.unrelated.legacy.event";
        await EventDeliveryPilotTestSupport.WriteRawLegacyOutboxEventAsync(
            serviceProvider,
            otherLegacyEventType,
            schemaVersion: 1,
            CancellationToken.None);

        // 步骤 3：请求目标 Pilot 流切流 → 必须成功（即使全局有 pending）
        Result<DeliveryCutoverResponse> cutoverResult;
        await using (var scope = EventDeliveryPilotTestSupport.CreateHostScope(serviceProvider))
        {
            var cutover = scope.ServiceProvider.GetRequiredService<DeliveryCutoverService>();
            cutoverResult = await cutover.CutoverAsync(
                new ChangeDeliveryOwnerRequest(
                    EventDeliveryPilotTestSupport.PilotEventType,
                    EventDeliveryPilotTestSupport.PilotSchemaVersion,
                    EventDeliveryOwner.CdcKafka,
                    "stream-scoped-cutover"),
                CancellationToken.None);
        }

        Assert.IsTrue(
            cutoverResult.IsSuccess,
            $"Expected success but got errors: {FormatErrors(cutoverResult)}");
        Assert.IsTrue(cutoverResult.Value!.OwnershipPersisted);
    }

    private static async Task VerifyCutoverFailsForTargetStreamBacklogAsync(
        DatabaseProvider provider,
        string connectionString)
    {
        // 场景：
        // 1) 目标 Pilot 流写一条 pending 消息（不处理，保持 pending）
        // 2) 请求切流 → 必须失败（返回 LegacyBacklogNotDrained），错误消息应指向目标流级别
        var options = new DatabaseOptions
        {
            Provider = provider,
            ConnectionString = connectionString,
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
        };
        await using var serviceProvider = await EventDeliveryPilotTestSupport
            .BuildPilotServicesAsync(options);

        // 步骤 1：只写入，不处理 → 目标流保持 pending
        await EventDeliveryPilotTestSupport.WritePilotOutboxEventAsync(
            serviceProvider,
            CancellationToken.None);

        // 步骤 2：请求切流 → 必须失败（积压未清空）
        Result<DeliveryCutoverResponse> cutoverResult;
        await using (var scope = EventDeliveryPilotTestSupport.CreateHostScope(serviceProvider))
        {
            var cutover = scope.ServiceProvider.GetRequiredService<DeliveryCutoverService>();
            cutoverResult = await cutover.CutoverAsync(
                new ChangeDeliveryOwnerRequest(
                    EventDeliveryPilotTestSupport.PilotEventType,
                    EventDeliveryPilotTestSupport.PilotSchemaVersion,
                    EventDeliveryOwner.CdcKafka,
                    "backlog-should-block-cutover"),
                CancellationToken.None);
        }

        Assert.IsFalse(cutoverResult.IsSuccess, "Expected cutover to fail for target stream pending backlog.");
        var errorCodes = cutoverResult.Error is null
            ? Array.Empty<string>()
            : new[] { cutoverResult.Error.Code };
        CollectionAssert.Contains(
            errorCodes,
            MessagingErrorCodes.LegacyBacklogNotDrained,
            $"Expected error code LegacyBacklogNotDrained, got: {string.Join(", ", errorCodes)}");
    }

    private static string FormatErrors<T>(Result<T> result)
    {
        if (result.IsSuccess || result.Error is null)
        {
            return "<success>";
        }

        return $"{result.Error.Code}: {result.Error.Message}";
    }
}
