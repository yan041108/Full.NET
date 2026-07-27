using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Host.Worker;
using NSubstitute;

namespace Full.NET.UnitTests.Outbox;

[TestClass]
public sealed class OutboxVersionRetirementScannerTests
{
    private static readonly DateTimeOffset Oldest =
        new(2026, 7, 27, 2, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Scan_includes_canonical_and_legacy_routes_and_blocks_when_any_message_remains()
    {
        var reader = Substitute.For<IOutboxBacklogReader>();
        reader.ReadVersionRetirementAsync(
                Arg.Is<IReadOnlyCollection<string>>(routes =>
                    routes != null
                    && routes.SequenceEqual(
                        new[] { TestHandler.Canonical, TestHandler.Legacy })),
                1,
                Arg.Any<CancellationToken>())
            .Returns(new OutboxVersionRetirementSnapshot(2, 1, Oldest));

        var report = await new OutboxVersionRetirementScanner(
                reader,
                [new TestHandler()])
            .ScanAsync(
                new OutboxVersionRetirementRequest(TestHandler.Canonical, 1),
                CancellationToken.None);

        Assert.AreEqual(OutboxVersionRetirementErrorCodes.Blocked, report.Code);
        Assert.IsFalse(report.CanRetire);
        CollectionAssert.AreEqual(
            new[] { TestHandler.Canonical, TestHandler.Legacy },
            report.Routes.ToArray());
        Assert.AreEqual(Oldest, report.OldestUnprocessedOccurredAtUtc);
    }

    [TestMethod]
    public async Task Scan_returns_safe_only_when_pending_and_dead_letter_counts_are_zero()
    {
        var reader = Substitute.For<IOutboxBacklogReader>();
        reader.ReadVersionRetirementAsync(
                Arg.Any<IReadOnlyCollection<string>>(),
                1,
                Arg.Any<CancellationToken>())
            .Returns(new OutboxVersionRetirementSnapshot(0, 0, null));

        var report = await new OutboxVersionRetirementScanner(
                reader,
                [new TestHandler()])
            .ScanAsync(
                new OutboxVersionRetirementRequest(TestHandler.Canonical, 1),
                CancellationToken.None);

        Assert.AreEqual(OutboxVersionRetirementErrorCodes.Safe, report.Code);
        Assert.IsTrue(report.CanRetire);
    }

    [TestMethod]
    public async Task Scan_rejects_a_route_without_a_registered_handler()
    {
        var scanner = new OutboxVersionRetirementScanner(
            Substitute.For<IOutboxBacklogReader>(),
            []);

        var exception =
            await Assert.ThrowsExactlyAsync<OutboxVersionRetirementException>(
                () => scanner.ScanAsync(
                    new OutboxVersionRetirementRequest("fullnet.missing", 1),
                    CancellationToken.None));

        Assert.AreEqual(
            OutboxVersionRetirementErrorCodes.HandlerNotFound,
            exception.Code);
    }

    private sealed class TestHandler : IIntegrationEventHandler
    {
        public const string Canonical =
            "fullnet.tests.messaging.version_retirement.current";

        public const string Legacy =
            "fullnet.tests.messaging.version-retirement.legacy";

        public string EventType => Canonical;

        public IReadOnlyList<string> LegacyEventTypes => [Legacy];

        public int SchemaVersion => 1;

        public Task HandleAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
