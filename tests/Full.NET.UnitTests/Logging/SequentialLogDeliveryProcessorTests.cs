using System.Text;
using Full.NET.LogConsumer;

namespace Full.NET.UnitTests.Logging;

[TestClass]
public sealed class SequentialLogDeliveryProcessorTests
{
    private const string EventId = "0199aa18-3e3b-7000-8000-5a8ab7a1f404";
    private static readonly KafkaLogInput ValidInput = new("logs.general", 1, 42, EventId,
        Encoding.UTF8.GetBytes($$"""
            {"@t":"2026-09-30T00:00:00Z","@mt":"ok","LogEventId":"{{EventId}}","log.class":"diagnostic","OccurredAtUtc":"2026-09-30T00:00:00Z","ExpiresAtUtc":"2026-10-30T00:00:00Z","IndexRouteVersion":2}
            """));

    [TestMethod]
    public async Task EsSuccessCommitsOnlyAfterDocumentConfirmation()
    {
        var sequence = new List<string>();
        var sink = new FakeDocumentSink((_, index) =>
        {
            Assert.AreEqual("fn-logs-2-diagnostic-2026.09.30", index);
            sequence.Add("es");
            return BulkItemOutcome.Succeeded;
        });
        var processor = NewProcessor(sink,
            new FakeDlq((_, _) => throw new AssertFailedException("DLQ should not run")),
            new FakeCommitter(input =>
            {
                Assert.AreEqual(42L, input.Offset);
                sequence.Add("commit");
            }));

        Assert.AreEqual(LogDeliveryDisposition.Completed,
            await processor.ProcessAsync(ValidInput));
        CollectionAssert.AreEqual(new[] { "es", "commit" }, sequence);
    }

    [TestMethod]
    public async Task TransientEsFailureRetainsOffsetAndDoesNotUseDlq()
    {
        var commits = 0;
        var processor = NewProcessor(
            new FakeDocumentSink((_, _) => BulkItemOutcome.Retry),
            new FakeDlq((_, _) => throw new AssertFailedException("DLQ should not run")),
            new FakeCommitter(_ => commits++));

        Assert.AreEqual(LogDeliveryDisposition.Retry,
            await processor.ProcessAsync(ValidInput));
        Assert.AreEqual(0, commits);
    }

    [TestMethod]
    public async Task PermanentEsFailureWaitsForDlqAckBeforeCommit()
    {
        var ack = false;
        var commits = 0;
        var processor = NewProcessor(
            new FakeDocumentSink((_, _) => BulkItemOutcome.Isolate),
            new FakeDlq((input, reason) =>
            {
                Assert.AreEqual(ValidInput, input);
                Assert.AreEqual(LogIsolationReason.PermanentSinkError, reason);
                return ack;
            }),
            new FakeCommitter(_ => commits++));

        Assert.AreEqual(LogDeliveryDisposition.Retry,
            await processor.ProcessAsync(ValidInput));
        Assert.AreEqual(0, commits);
        ack = true;
        Assert.AreEqual(LogDeliveryDisposition.Completed,
            await processor.ProcessAsync(ValidInput));
        Assert.AreEqual(1, commits);
    }

    [TestMethod]
    public async Task InvalidRecordRequiresDlqAckAndCannotReachEs()
    {
        var input = ValidInput with { Value = Encoding.UTF8.GetBytes("not-json") };
        var sequence = new List<string>();
        var processor = NewProcessor(
            new FakeDocumentSink((_, _) => throw new AssertFailedException("ES should not run")),
            new FakeDlq((record, reason) =>
            {
                Assert.AreEqual(input, record);
                Assert.AreEqual(LogIsolationReason.InvalidRecord, reason);
                sequence.Add("dlq");
                return true;
            }),
            new FakeCommitter(_ => sequence.Add("commit")));

        Assert.AreEqual(LogDeliveryDisposition.Completed, await processor.ProcessAsync(input));
        CollectionAssert.AreEqual(new[] { "dlq", "commit" }, sequence);
    }

    [TestMethod]
    public async Task ExpiredRecordIsolatedWithoutEsWrite()
    {
        var sequence = new List<string>();
        var processor = new SequentialLogDeliveryProcessor(
            new FakeDocumentSink((_, _) => throw new AssertFailedException("ES should not run")),
            new FakeDlq((_, reason) =>
            {
                Assert.AreEqual(LogIsolationReason.Unrouteable, reason);
                sequence.Add("dlq");
                return true;
            }),
            new FakeCommitter(_ => sequence.Add("commit")),
            new Dictionary<int, int> { [2] = 30 }, 1024,
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero)));

        Assert.AreEqual(LogDeliveryDisposition.Completed,
            await processor.ProcessAsync(ValidInput));
        CollectionAssert.AreEqual(new[] { "dlq", "commit" }, sequence);
    }

    [TestMethod]
    public async Task CommitFailurePropagatesAfterSinkAckForIdempotentReplay()
    {
        var writes = 0;
        var processor = NewProcessor(
            new FakeDocumentSink((_, _) =>
            {
                writes++;
                return BulkItemOutcome.Succeeded;
            }),
            new FakeDlq((_, _) => throw new AssertFailedException("DLQ should not run")),
            new FakeCommitter(_ => throw new InvalidOperationException("commit failed")));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => processor.ProcessAsync(ValidInput));
        Assert.AreEqual(1, writes);
    }

    private static SequentialLogDeliveryProcessor NewProcessor(
        ILogDocumentSink sink,
        ILogDeadLetterSink dlq,
        ILogOffsetCommitter committer) =>
        new(sink, dlq, committer, new Dictionary<int, int> { [2] = 30 }, 1024,
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)));

    private sealed class FakeDocumentSink(Func<ParsedLogRecord, string, BulkItemOutcome> handle)
        : ILogDocumentSink
    {
        public Task<BulkItemOutcome> WriteAsync(ParsedLogRecord record, string indexName,
            CancellationToken cancellationToken) => Task.FromResult(handle(record, indexName));
    }

    private sealed class FakeDlq(Func<KafkaLogInput, LogIsolationReason, bool> handle)
        : ILogDeadLetterSink
    {
        public Task<bool> PublishAsync(KafkaLogInput input, LogIsolationReason reason,
            CancellationToken cancellationToken) => Task.FromResult(handle(input, reason));
    }

    private sealed class FakeCommitter(Action<KafkaLogInput> commit) : ILogOffsetCommitter
    {
        public void CommitNext(KafkaLogInput input) => commit(input);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
