using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Data.Dapper.Outbox;
using Full.NET.Messaging.Abstractions;
using NSubstitute;

namespace Full.NET.UnitTests.Data;

/// <summary>
/// 验证按事件流所有权路由 Outbox 写入的路由器。
/// </summary>
[TestClass]
public sealed class DapperRoutedOutboxWriterTests
{
    private static readonly Guid SampleTenantId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private const string LegacyStream = "legacy.stream.changed";
    private const string CdcStream = "fullnet.organization.unit.changed";
    private const string UnknownStream = "unknown.stream.happened";

    [TestMethod]
    public async Task AddAsync_without_metadata_routes_to_legacy_writer_for_legacy_owner()
    {
        var counters = new OutboxCallCounters();
        var c = CreateRoutedCollaborators(
            counters,
            legacyOwners: [LegacyStream]);

        var routed = new DapperRoutedOutboxWriter(c.Legacy, c.Append, c.Resolver, c.Gate);
        var payload = new SamplePayload(42, "hello");

        await routed.AddAsync(
            LegacyStream,
            1,
            payload,
            CancellationToken.None);

        Assert.AreEqual(1, counters.LegacyPlainCalls);
        Assert.AreEqual(0, counters.LegacyMetadataCalls);
        Assert.AreEqual(0, counters.AppendPlainCalls);
        Assert.AreEqual(0, counters.AppendMetadataCalls);
    }

    [TestMethod]
    public async Task
        AddAsync_with_metadata_routes_to_append_only_writer_for_cdc_kafka_owner()
    {
        var metadata = IntegrationEventMetadata.Create(
            partitionKey: SampleTenantId.ToString("D"),
            producer: "fullnet.organization");
        var counters = new OutboxCallCounters();
        var c = CreateRoutedCollaborators(
            counters,
            cdcOwners: [CdcStream]);

        var routed = new DapperRoutedOutboxWriter(c.Legacy, c.Append, c.Resolver, c.Gate);
        var payload = new SamplePayload(7, "org-unit-42");

        await routed.AddAsync(
            CdcStream,
            1,
            payload,
            metadata,
            CancellationToken.None);

        Assert.AreEqual(0, counters.LegacyPlainCalls);
        Assert.AreEqual(0, counters.LegacyMetadataCalls);
        Assert.AreEqual(0, counters.AppendPlainCalls);
        Assert.AreEqual(1, counters.AppendMetadataCalls);
    }

    [TestMethod]
    public async Task Unknown_stream_falls_back_to_directory_default_owner_and_writes_legacy()
    {
        var counters = new OutboxCallCounters();
        var c = CreateRoutedCollaborators(counters);

        var routed = new DapperRoutedOutboxWriter(c.Legacy, c.Append, c.Resolver, c.Gate);
        var payload = new SamplePayload(1, "unregistered-event");

        await routed.AddAsync(
            UnknownStream,
            2,
            payload,
            CancellationToken.None);

        Assert.AreEqual(1, counters.LegacyPlainCalls);
        Assert.AreEqual(0, counters.LegacyMetadataCalls);
        Assert.AreEqual(0, counters.AppendPlainCalls);
        Assert.AreEqual(0, counters.AppendMetadataCalls);
    }

    [TestMethod]
    public async Task Cdc_kafka_owner_without_metadata_throws_fail_closed()
    {
        var counters = new OutboxCallCounters();
        var c = CreateRoutedCollaborators(
            counters,
            cdcOwners: [CdcStream]);

        var routed = new DapperRoutedOutboxWriter(c.Legacy, c.Append, c.Resolver, c.Gate);
        var payload = new SamplePayload(3, "must-fail");

        try
        {
            await routed.AddAsync(
                CdcStream,
                1,
                payload,
                CancellationToken.None);
            Assert.Fail("Expected exception not thrown.");
        }
        catch (InvalidOperationException ex)
        {
            StringAssert.Contains(
                ex.Message,
                IntegrationEventFailureCodes.OutboxEventMetadataMissing);
        }
        Assert.AreEqual(0, counters.LegacyPlainCalls);
        Assert.AreEqual(0, counters.LegacyMetadataCalls);
        Assert.AreEqual(0, counters.AppendPlainCalls);
        Assert.AreEqual(0, counters.AppendMetadataCalls);
    }

    [TestMethod]
    public async Task Single_call_writes_exactly_one_outbox_table()
    {
        var metadata = IntegrationEventMetadata.Create(
            partitionKey: SampleTenantId.ToString("D"),
            producer: "fullnet.organization");
        var counters = new OutboxCallCounters();
        var c = CreateRoutedCollaborators(
            counters,
            legacyOwners: [LegacyStream],
            cdcOwners: [CdcStream]);

        var routed = new DapperRoutedOutboxWriter(c.Legacy, c.Append, c.Resolver, c.Gate);
        var payload = new SamplePayload(11, "both-registered-separately");

        await routed.AddAsync(
            LegacyStream,
            1,
            payload,
            CancellationToken.None);
        await routed.AddAsync(
            CdcStream,
            1,
            payload,
            metadata,
            CancellationToken.None);

        Assert.AreEqual(1, counters.LegacyPlainCalls);
        Assert.AreEqual(0, counters.LegacyMetadataCalls);
        Assert.AreEqual(0, counters.AppendPlainCalls);
        Assert.AreEqual(1, counters.AppendMetadataCalls);
    }

    private sealed class OutboxCallCounters
    {
        public int LegacyPlainCalls;
        public int LegacyMetadataCalls;
        public int AppendPlainCalls;
        public int AppendMetadataCalls;
    }

    private sealed record Collaborators(
        DapperOutboxWriter Legacy,
        DapperAppendOnlyOutboxWriter Append,
        IEffectiveEventDeliveryOwnerResolver Resolver,
        IEventStreamOwnershipGate Gate);

    private static Collaborators CreateRoutedCollaborators(
        OutboxCallCounters counters,
        string[]? legacyOwners = null,
        string[]? cdcOwners = null)
    {
        ArgumentNullException.ThrowIfNull(counters);

        var resolver = Substitute.For<IEffectiveEventDeliveryOwnerResolver>();
        var gate = Substitute.For<IEventStreamOwnershipGate>();
        gate.AcquireProducerAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        foreach (var stream in legacyOwners ?? Array.Empty<string>())
        {
            resolver
                .GetDeliveryOwnerAsync(stream, Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(EventDeliveryOwner.LegacyPolling));
        }
        foreach (var stream in cdcOwners ?? Array.Empty<string>())
        {
            resolver
                .GetDeliveryOwnerAsync(stream, Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(EventDeliveryOwner.CdcKafka));
        }
        resolver
            .GetDeliveryOwnerAsync(
                Arg.Is<string>(s =>
                    (legacyOwners == null || !legacyOwners.Contains(s))
                    && (cdcOwners == null || !cdcOwners.Contains(s))),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EventDeliveryOwner.LegacyPolling));

        var idGen = Substitute.For<IIdGenerator>();
        idGen.NewId().Returns(Guid.NewGuid());
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns(SampleTenantId);
        var serializer = Substitute.For<IIntegrationEventSerializer>();
        serializer.ContentType.Returns("application/json");
        serializer
            .Serialize(Arg.Any<object>())
            .Returns(new byte[] { 1, 2, 3 });
        var commandExecutor = Substitute.For<ICommandExecutor>();
        commandExecutor
            .ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var legacy = Substitute.For<DapperOutboxWriter>(
            commandExecutor, serializer, idGen, tenant, clock);
        var append = Substitute.For<DapperAppendOnlyOutboxWriter>(
            commandExecutor, serializer, idGen, tenant, clock);

        legacy.When(x => x.AddAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => counters.LegacyPlainCalls++);
        legacy.When(x => x.AddAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<object>(),
                Arg.Any<IntegrationEventMetadata>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => counters.LegacyMetadataCalls++);
        append.When(x => x.AddAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => counters.AppendPlainCalls++);
        append.When(x => x.AddAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<object>(),
                Arg.Any<IntegrationEventMetadata>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => counters.AppendMetadataCalls++);

        return new Collaborators(legacy, append, resolver, gate);
    }

    internal sealed record SamplePayload(int Id, string Name);
}
