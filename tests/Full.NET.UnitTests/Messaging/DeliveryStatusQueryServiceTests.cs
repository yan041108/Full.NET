using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging.Features.ChangeDeliveryOwner;
using Full.NET.Modules.Messaging.Features.GetDeliveryStatus;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Messaging;

/// <summary>总览区分完整零值、读取失败及截断，不用本地配置声明运行就绪。</summary>
[TestClass]
public sealed class DeliveryStatusQueryServiceTests
{
    [TestMethod]
    public async Task Complete_empty_query_confirms_zero_but_never_claims_runtime_ready()
    {
        var f = new Fixture(); var result = (await f.Service.GetAsync(default)).Value!;
        Assert.AreEqual("available", result.ObservationState); Assert.AreEqual("not_observed", result.TargetRuntimeState);
        Assert.AreEqual(0L, result.Streams.Single().LegacyBacklog!.PendingCount);
        Assert.AreEqual(0L, result.Streams.Single().KafkaProjectedDeadLetterCount);
        Assert.AreEqual("blocked", result.Streams.Single().CutoverReadiness);
        CollectionAssert.Contains(result.Streams.Single().BlockingReasons.ToArray(), "ownership_record_required");
    }

    [TestMethod]
    public async Task Failed_sources_remain_null_and_do_not_become_zero_or_ready()
    {
        var f = new Fixture();
        f.Backlog.ReadBacklogAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<OutboxBacklogSnapshot>(new TimeoutException()));
        f.Backlog.ReadStreamBacklogsAsync(512, Arg.Any<CancellationToken>()).Returns(Task.FromException<IReadOnlyList<OutboxStreamBacklogSnapshot>>(new TimeoutException()));
        f.Query.Fail = true;
        var result = (await f.Service.GetAsync(default)).Value!;
        Assert.AreEqual("partial", result.ObservationState); Assert.IsNull(result.Backlog); Assert.IsNull(result.Streams.Single().LegacyBacklog);
        Assert.IsNull(result.Streams.Single().KafkaProjectedDeadLetterCount); Assert.IsNull(result.UnattributedKafkaDeadLetters);
    }

    [TestMethod]
    public async Task Truncated_legacy_query_does_not_infer_missing_stream_is_empty()
    {
        var f = new Fixture();
        f.Backlog.ReadStreamBacklogsAsync(512, Arg.Any<CancellationToken>()).Returns(Enumerable.Range(0, 513).Select(index =>
            new OutboxStreamBacklogSnapshot("fullnet.aaa." + index.ToString("D4"), 1, new(1, null))).ToArray());
        var result = (await f.Service.GetAsync(default)).Value!;
        Assert.IsTrue(result.HasMoreStreams); Assert.AreEqual(512, result.Streams.Count); Assert.AreEqual("partial", result.ObservationState);
        Assert.IsTrue(result.Streams.All(item => item.CutoverReadiness != "ready"));
    }

    [TestMethod]
    public async Task Cancellation_propagates_instead_of_reporting_healthy_empty_data()
    {
        var f = new Fixture();
        f.Backlog.ReadBacklogAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<OutboxBacklogSnapshot>(new OperationCanceledException()));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Service.GetAsync(default));
    }
    private sealed class Fixture
    {
        public readonly IOutboxBacklogReader Backlog = Substitute.For<IOutboxBacklogReader>();
        public readonly ObservationQuery Query = new();
        public readonly DeliveryStatusQueryService Service;
        public Fixture()
        {
            var topic = IntegrationEventTopicDefinition.Create("messaging.test.v1", "fullnet.messaging.test.event", 1, EventDeliveryOwner.LegacyPolling);
            Backlog.ReadBacklogAsync(Arg.Any<CancellationToken>()).Returns(new OutboxBacklogSnapshot(0, null));
            Backlog.ReadStreamBacklogsAsync(512, Arg.Any<CancellationToken>()).Returns(Array.Empty<OutboxStreamBacklogSnapshot>());
            var owners = Substitute.For<IEffectiveEventDeliveryOwnerResolver>();
            owners.GetDeliveryOwnersAsync(Arg.Any<IReadOnlyCollection<IntegrationEventTopicDefinition>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<(string, int), EventDeliveryOwner> { [(topic.EventType, 1)] = EventDeliveryOwner.LegacyPolling });
            Service = new(Backlog, owners, [topic], Query, Options.Create(new DatabaseOptions()), Options.Create(new DeliveryCutoverOptions()),
                new(false, 100, TimeSpan.FromSeconds(30)), new FailClosedEventDeliveryRollbackReadinessReader(), Substitute.For<IIntegrationEventSubscriptionCatalog>(), Options.Create(new ShadowCaptureOptions()));
        }
    }
    private sealed class ObservationQuery : IQueryExecutor
    {
        public bool Fail { get; set; }
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            Fail ? Task.FromException<IReadOnlyList<T>>(new TimeoutException()) : Task.FromResult<IReadOnlyList<T>>([]);
    }
}
