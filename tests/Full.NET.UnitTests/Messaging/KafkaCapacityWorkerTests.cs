using Full.NET.Benchmarks.Kafka;
using Full.NET.Abstractions.Messaging;
using Full.NET.Messaging.Abstractions;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaCapacityWorkerTests
{
    [TestMethod]
    public async Task Handler_accepts_valid_scope_B_payloads_without_double_counting()
    {
        var observer = new KafkaCapacityWorkerObserver(maximumMessages: 4);
        var handler = new KafkaCapacityWorkerSubscription(observer);
        var payload = KafkaCapacityEnvelopeCodec.Encode(
            128,
            runHash: 17,
            sampleHash: 29,
            globalSequence: 2,
            partitionSequence: 1,
            scheduledTimestamp: 100,
            enqueuedTimestamp: 120);
        var context = new IntegrationEventContext(
            Guid.CreateVersion7(),
            KafkaCapacityWorkerContracts.EventType,
            KafkaCapacityWorkerContracts.SchemaVersion,
            TenantId: null,
            TraceId: null,
            OccurredAtUtc: DateTimeOffset.UtcNow);

        await handler.HandleAsync(context, payload, CancellationToken.None);
        await handler.HandleAsync(context, payload, CancellationToken.None);

        var snapshot = observer.Snapshot();
        Assert.AreEqual(1, snapshot.Processed);
        Assert.AreEqual(0, snapshot.Corrupted);
    }

    [TestMethod]
    public async Task Handler_rejects_payload_with_wrong_run_identity()
    {
        var observer = new KafkaCapacityWorkerObserver(maximumMessages: 4);
        observer.BeginPhase(runHash: 1, sampleHash: 2);
        var handler = new KafkaCapacityWorkerSubscription(observer);
        var payload = KafkaCapacityEnvelopeCodec.Encode(
            128,
            runHash: 9,
            sampleHash: 2,
            globalSequence: 0,
            partitionSequence: 0,
            scheduledTimestamp: 100,
            enqueuedTimestamp: 100);
        var context = new IntegrationEventContext(
            Guid.CreateVersion7(),
            KafkaCapacityWorkerContracts.EventType,
            KafkaCapacityWorkerContracts.SchemaVersion,
            TenantId: null,
            TraceId: null,
            OccurredAtUtc: DateTimeOffset.UtcNow);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            handler.HandleAsync(context, payload, CancellationToken.None));

        Assert.AreEqual(1, observer.Snapshot().Corrupted);
    }
}
