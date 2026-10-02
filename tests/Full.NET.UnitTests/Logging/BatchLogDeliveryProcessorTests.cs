using System.Text;
using Full.NET.LogConsumer;

namespace Full.NET.UnitTests.Logging;

[TestClass]
public sealed class BatchLogDeliveryProcessorTests
{
    private static KafkaLogInput Input(long offset, int partition = 0)
    {
        var id = Guid.CreateVersion7().ToString("D");
        return new("logs", partition, offset, id, Encoding.UTF8.GetBytes($$"""
        {"@t":"2026-09-30T00:00:00Z","@mt":"batch","LogEventId":"{{id}}","log.class":"diagnostic","OccurredAtUtc":"2026-09-30T00:00:00Z","ExpiresAtUtc":"2026-10-30T00:00:00Z","IndexRouteVersion":2}
        """));
    }

    [TestMethod]
    public async Task RetryCommitsOnlyCompletedPrefixAndNeverLaterSuccess()
    {
        var commits = new List<long>();
        var processor = New([BulkItemOutcome.Succeeded, BulkItemOutcome.Retry, BulkItemOutcome.Succeeded],
            _ => throw new AssertFailedException("No DLQ"), input => commits.Add(input.Offset));
        var result = await processor.ProcessAsync([Input(10), Input(11), Input(12)], () => true);
        Assert.AreEqual(new BatchLogDeliveryResult(LogDeliveryDisposition.Retry, 1), result);
        CollectionAssert.AreEqual(new[] { 10L }, commits);
    }

    [TestMethod]
    public async Task IsolationWaitsForAckAndCommitsOnePartitionOnce()
    {
        foreach (var ack in new[] { false, true })
        {
            var commits = new List<long>();
            var processor = New([BulkItemOutcome.Succeeded, BulkItemOutcome.Isolate, BulkItemOutcome.Succeeded],
                reason => { Assert.AreEqual(LogIsolationReason.PermanentSinkError, reason); return ack; }, input => commits.Add(input.Offset));
            var result = await processor.ProcessAsync([Input(10), Input(11), Input(12)], () => true);
            Assert.AreEqual(ack ? 3 : 1, result.CommittedRecords);
            Assert.AreEqual(ack ? LogDeliveryDisposition.Completed : LogDeliveryDisposition.Retry, result.Disposition);
            CollectionAssert.AreEqual(new[] { ack ? 12L : 10L }, commits);
        }
    }

    [TestMethod]
    public async Task NumericGapsAreAllowedButPartitionOrderAndBatchCountAreBounded()
    {
        var commits = new List<(int Partition, long Offset)>();
        var processor = New([BulkItemOutcome.Succeeded, BulkItemOutcome.Succeeded, BulkItemOutcome.Succeeded],
            _ => false, input => commits.Add((input.Partition, input.Offset)));
        var result = await processor.ProcessAsync([Input(10), Input(2, 1), Input(14)], () => true);
        Assert.AreEqual(3, result.CommittedRecords);
        CollectionAssert.AreEquivalent(new[] { (0, 14L), (1, 2L) }, commits);
        await Assert.ThrowsAsync<ArgumentException>(() => processor.ProcessAsync([Input(10), Input(9)], () => true));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => processor.ProcessAsync([], () => true));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => processor.ProcessAsync(Enumerable.Range(0, 9).Select(i => Input(i)).ToArray(), () => true));
    }

    [TestMethod]
    public async Task LostAssignmentAfterEsOrDlqMustNotCommit()
    {
        foreach (var lostAtDlq in new[] { false, true })
        {
            var owns = true;
            var sink = new FakeSink(_ => { if (!lostAtDlq) owns = false; return [BulkItemOutcome.Isolate]; });
            var processor = new BatchLogDeliveryProcessor(sink, new FakeDlq(_ => { owns = false; return true; }),
                new FakeCommitter(_ => throw new AssertFailedException("Stale assignment must not commit")),
                new Dictionary<int, int> { [2] = 30 }, 1024, new Clock());
            Assert.AreEqual(new BatchLogDeliveryResult(LogDeliveryDisposition.Retry, 0),
                await processor.ProcessAsync([Input(10)], () => owns));
        }
    }

    [TestMethod]
    public async Task PoisonRecordCannotBeSkippedWithoutReliableIsolation()
    {
        var commits = 0;
        var processor = New([BulkItemOutcome.Succeeded], reason =>
            { Assert.AreEqual(LogIsolationReason.InvalidRecord, reason); return false; }, _ => commits++);
        var poison = Input(10) with { Value = Encoding.UTF8.GetBytes("not-json") };
        Assert.AreEqual(new BatchLogDeliveryResult(LogDeliveryDisposition.Retry, 0),
            await processor.ProcessAsync([poison, Input(11)], () => true));
        Assert.AreEqual(0, commits);
    }

    [TestMethod]
    public async Task CancelledBatchAndUnknownResponseNeverCommit()
    {
        var processor = New([], _ => false, _ => throw new AssertFailedException("No commit"));
        Assert.AreEqual(new BatchLogDeliveryResult(LogDeliveryDisposition.Retry, 0),
            await processor.ProcessAsync([Input(10)], () => true));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => processor.ProcessAsync([Input(10)], () => true, cancelled.Token));
    }

    private static BatchLogDeliveryProcessor New(BulkItemOutcome[] outcomes, Func<LogIsolationReason, bool> ack, Action<KafkaLogInput> commit)
        => new(new FakeSink(_ => outcomes), new FakeDlq(ack), new FakeCommitter(commit), new Dictionary<int, int> { [2] = 30 }, 1024, new Clock());

    [TestMethod]
    public async Task OwnershipLossBetweenPartitionCommitsReportsOnlyConfirmedRecords()
    {
        var owns = true;
        var commits = new List<int>();
        var processor = New([BulkItemOutcome.Succeeded, BulkItemOutcome.Succeeded], _ => false,
            input => { commits.Add(input.Partition); owns = false; });
        var result = await processor.ProcessAsync([Input(10), Input(2, 1)], () => owns);
        Assert.AreEqual(new BatchLogDeliveryResult(LogDeliveryDisposition.Retry, 1), result);
        CollectionAssert.AreEqual(new[] { 0 }, commits);
    }

    [TestMethod]
    public async Task CancellationAfterReceiptAndCommitFailureCannotReturnCompleted()
    {
        using var cancelled = new CancellationTokenSource();
        var processor = new BatchLogDeliveryProcessor(
            new FakeSink(_ => { cancelled.Cancel(); return [BulkItemOutcome.Succeeded]; }),
            new FakeDlq(_ => false), new FakeCommitter(_ => throw new AssertFailedException("No commit after cancellation")),
            new Dictionary<int, int> { [2] = 30 }, 1024, new Clock());
        await Assert.ThrowsAsync<OperationCanceledException>(() => processor.ProcessAsync([Input(10)], () => true, cancelled.Token));
        processor = New([BulkItemOutcome.Succeeded], _ => false, _ => throw new InvalidOperationException("Broker rejected commit"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync([Input(10)], () => true));
    }
    private sealed class FakeSink(Func<IReadOnlyList<LogDocumentWrite>, BulkItemOutcome[]> result) : ILogDocumentBatchSink
    {
        public Task<BulkItemOutcome[]> WriteBatchAsync(IReadOnlyList<LogDocumentWrite> writes, CancellationToken cancellationToken)
            => Task.FromResult(result(writes));
    }
    private sealed class FakeDlq(Func<LogIsolationReason, bool> ack) : ILogDeadLetterSink
    {
        public Task<bool> PublishAsync(KafkaLogInput input, LogIsolationReason reason, CancellationToken cancellationToken) => Task.FromResult(ack(reason));
    }
    private sealed class FakeCommitter(Action<KafkaLogInput> commit) : ILogOffsetCommitter { public void CommitNext(KafkaLogInput input) => commit(input); }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero); }
}
