using System.Data.Common;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging.Features.ChangeDeliveryOwner;
using Full.NET.Modules.Messaging.Features.GetDeliveryStatus;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>总览真实聚合的双库边界：重试、租约、死信、已处理排除、未知流和无法归属的投影。</summary>
[TestClass]
public sealed class MessagingDeliveryOverviewTests
{
    [TestMethod]
    public async Task SqlServer_overview_aggregates_streams_and_guards_host_scope() => await VerifyAsync(new()
        { Provider = DatabaseProvider.SqlServer, ConnectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() });
    [TestMethod]
    public async Task MySql_overview_aggregates_streams_and_guards_host_scope() => await VerifyAsync(new()
        { Provider = DatabaseProvider.MySql, ConnectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16 });

    private static async Task VerifyAsync(DatabaseOptions database)
    {
        await using var provider = await EventDeliveryPilotTestSupport.BuildPilotServicesAsync(database, cutoverEnabled: false, seedCutoverEvidence: false);
        await using DbConnection connection = database.Provider == DatabaseProvider.SqlServer ? new SqlConnection(database.ConnectionString)
            : new MySqlConnection(new MySqlConnectionStringBuilder(database.ConnectionString) { GuidFormat = MySqlGuidFormat.Binary16, AllowUserVariables = true }.ConnectionString);
        await connection.OpenAsync();
        var now = DateTime.UtcNow;
        var known = EventDeliveryPilotTestSupport.PilotEventType;
        const string unknown = "fullnet.messaging.unknown.event";
        foreach (var (type, status) in new[] { (known, "due"), (known, "leased"), (known, "dead"), (known, "processed"), (unknown, "due") })
            await connection.ExecuteAsync("""
                INSERT INTO fn_outbox_message (Id, MessageType, SchemaVersion, ContentType, TenantId, TraceId, Payload, OccurredAtUtc,
                    ProcessedAtUtc, NextAttemptAtUtc, Attempts, LockId, LockedUntilUtc, Error, DeadLetteredAtUtc)
                VALUES (@Id, @Type, 1, 'application/x-memorypack', NULL, NULL, @Payload, @OccurredAtUtc,
                    @ProcessedAtUtc, @NextAttemptAtUtc, 2, @LockId, @LockedUntilUtc, NULL, @DeadLetteredAtUtc)
                """, new { Id = Guid.CreateVersion7(), Type = type, Payload = new byte[] { 1 }, OccurredAtUtc = now.AddHours(-2),
                    ProcessedAtUtc = status == "processed" ? now : (DateTime?)null, NextAttemptAtUtc = now.AddMinutes(-1),
                    LockId = status == "leased" ? Guid.CreateVersion7() : (Guid?)null, LockedUntilUtc = status == "leased" ? now.AddHours(1) : (DateTime?)null,
                    DeadLetteredAtUtc = status == "dead" ? now : (DateTime?)null });
        foreach (var type in new[] { known, null })
            await connection.ExecuteAsync("""
                INSERT INTO fn_messaging_dead_letter (Id, ConsumerName, DlqTopic, DlqPartition, DlqOffset, SourceTopic,
                    SourcePartition, SourceOffset, MessageId, MessageType, SchemaVersion, TenantId, Attempts, ErrorCode,
                    FirstFailedAtUtc, LastFailedAtUtc, CanReplay, ReplayedAtUtc, SnapshotJson)
                VALUES (@Id, 'fullnet.messaging.overview', 'messaging.overview.v1.dlq', 0, @Position, 'messaging.overview.v1',
                    0, @Position, NULL, @Type, @Version, NULL, 1, 'messaging.test.projection', @Now, @Now, 0, NULL, '{}')
                """, new { Id = Guid.CreateVersion7(), Type = type, Version = type is null ? (int?)null : 1, Position = type is null ? 1L : 0L, Now = now });
        await using var scope = EventDeliveryPilotTestSupport.CreateHostScope(provider);
        var services = scope.ServiceProvider;
        var backlog = services.GetRequiredService<IOutboxBacklogReader>();
        var query = new DeliveryStatusQueryService(backlog, services.GetRequiredService<IEffectiveEventDeliveryOwnerResolver>(),
            services.GetServices<IntegrationEventTopicDefinition>(), services.GetRequiredService<IQueryExecutor>(),
            services.GetRequiredService<IOptions<DatabaseOptions>>(), services.GetRequiredService<IOptions<DeliveryCutoverOptions>>(),
            new(false, 100, TimeSpan.FromSeconds(30)), services.GetRequiredService<IEventDeliveryRollbackReadinessReader>(),
            services.GetRequiredService<IntegrationEventSubscriptionCatalog>(), services.GetRequiredService<IOptions<ShadowCaptureOptions>>());
        var result = (await query.GetAsync(default)).Value!;
        Assert.AreEqual(3L, result.Backlog!.PendingCount); Assert.AreEqual(1L, result.Backlog.DeadLetterCount);
        var stream = result.Streams.Single(item => item.EventType == known);
        Assert.AreEqual(2L, stream.LegacyBacklog!.PendingCount); Assert.AreEqual(1L, stream.LegacyBacklog.DueRetryCount);
        Assert.AreEqual(1L, stream.LegacyBacklog.ActiveLeaseCount); Assert.AreEqual(1L, stream.LegacyBacklog.DeadLetterCount);
        Assert.IsNotNull(stream.LegacyBacklog.OldestOccurredAtUtc); Assert.AreEqual(TimeSpan.Zero, stream.LegacyBacklog.OldestOccurredAtUtc.Value.Offset);
        Assert.AreEqual(1L, stream.KafkaProjectedDeadLetterCount); Assert.IsNotNull(stream.OldestKafkaDeadLetterAtUtc);
        Assert.AreEqual(0L, stream.RetainedEventCount); Assert.AreEqual(1L, result.UnattributedKafkaDeadLetters);
        Assert.IsFalse(result.Streams.Single(item => item.EventType == unknown).IsRegistered);
        var bounded = await backlog.ReadStreamBacklogsAsync(1, default); Assert.AreEqual(2, bounded.Count, "多出的一行必须保留为截断哨兵。");
        services.GetRequiredService<ICurrentTenantContextWriter>().SetTenant(new(Guid.CreateVersion7(), "tenant", "租户测试"));
        await Assert.ThrowsExactlyAsync<HostContextRequiredException>(() => backlog.ReadStreamBacklogsAsync(10, default));
    }
}
