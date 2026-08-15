using System.Diagnostics;
using Confluent.Kafka;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 通过业务事务写入 append-only Outbox，并记录 Outbox 提交延迟。
/// </summary>
public sealed class KafkaCapacityOutboxProducer
{
    public async Task WriteCommittedAsync(
        IServiceScope scope,
        KafkaCapacitySampleContext context,
        long globalSequence,
        long partitionSequence,
        long scheduledTimestampMicroseconds,
        long enqueuedTimestampMicroseconds,
        KafkaCapacityLatencyHistogram outboxCommitLatency,
        CancellationToken cancellationToken)
    {
        var partition = checked((int)(globalSequence % context.TopicIdentity.Partitions));
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var payload = KafkaCapacityEnvelopeCodec.Encode(
            context.Sample.PayloadSizeBytes,
            context.RunHash,
            context.SampleHash,
            globalSequence,
            partitionSequence,
            scheduledTimestampMicroseconds,
            enqueuedTimestampMicroseconds);
        var partitionKey = $"capacity-{partition}";
        var metadata = IntegrationEventMetadata.Create(
            partitionKey,
            "fullnet.capacity.runner",
            correlationId: $"capacity-{globalSequence}");
        var transaction = scope.ServiceProvider.GetRequiredService<ICommandTransaction>();
        var commitStarted = Stopwatch.GetTimestamp();
        await transaction.ExecuteAsync(
            async token =>
            {
                await scope.ServiceProvider.GetRequiredService<IOutboxWriter>()
                    .AddAsync(
                        KafkaCapacityWorkerContracts.EventType,
                        KafkaCapacityWorkerContracts.SchemaVersion,
                        payload,
                        metadata,
                        token)
                    .ConfigureAwait(false);
                return 0;
            },
            cancellationToken).ConfigureAwait(false);
        var committedMicroseconds = Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp()).Ticks / 10
            - enqueuedTimestampMicroseconds;
        if (!outboxCommitLatency.RecordMicroseconds(Math.Max(1, committedMicroseconds)))
        {
            throw new InvalidOperationException("Outbox commit latency histogram overflow.");
        }
    }
}

/// <summary>
/// 统计 Debezium 路由 Topic 上已发布且负载可解码的 CDC 消息。
/// </summary>
public sealed class KafkaCapacityCdcTracker : IAsyncDisposable
{
    private readonly KafkaEnvelopeReader envelopeReader = new();
    private readonly long[] publishedBits;
    private readonly long[] committedBits;
    private long published;
    private uint expectedRunHash;
    private uint expectedSampleHash;
    private KafkaCapacityLatencyHistogram cdcToKafkaLatency = new();
    private readonly Dictionary<long, long> outboxCommittedAt = new();
    private readonly object gate = new();

    public KafkaCapacityCdcTracker(int maximumMessages)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumMessages);
        publishedBits = new long[checked((maximumMessages + 63) / 64)];
        committedBits = new long[checked((maximumMessages + 63) / 64)];
    }

    public void BeginPhase(uint runHash, uint sampleHash)
    {
        expectedRunHash = runHash;
        expectedSampleHash = sampleHash;
        Array.Clear(publishedBits);
        Array.Clear(committedBits);
        Volatile.Write(ref published, 0);
        cdcToKafkaLatency = new KafkaCapacityLatencyHistogram();
        lock (gate)
        {
            outboxCommittedAt.Clear();
        }
    }

    public void NoteOutboxCommitted(long globalSequence, long committedTimestampMicroseconds)
    {
        if (globalSequence < 0 || globalSequence >= committedBits.Length * 64L)
        {
            return;
        }

        var word = (int)(globalSequence >> 6);
        var mask = 1L << ((int)globalSequence & 63);
        lock (gate)
        {
            committedBits[word] |= mask;
            outboxCommittedAt[globalSequence] = committedTimestampMicroseconds;
        }
    }

    public bool OnKafkaMessage(ConsumeResult<string, byte[]> consumed)
    {
        if (KafkaCapacityEnvelopePayloadDecoder.TryDecode(consumed.Message.Value, out var envelope))
        {
            if (envelope.RunHash != expectedRunHash
                || envelope.SampleHash != expectedSampleHash)
            {
                return false;
            }

            lock (gate)
            {
                if (outboxCommittedAt.TryGetValue(envelope.GlobalSequence, out var committedAt))
                {
                    var lag = Math.Max(
                        1,
                        Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp()).Ticks / 10 - committedAt);
                    cdcToKafkaLatency.RecordMicroseconds(lag);
                }
            }

            return MarkPublished(envelope.GlobalSequence);
        }

        if (envelopeReader.TryRead(consumed, out var integration, out _)
            && integration is not null
            && KafkaCapacityEnvelopePayloadDecoder.TryDecode(integration.Payload, out envelope))
        {
            if (envelope.RunHash != expectedRunHash
                || envelope.SampleHash != expectedSampleHash)
            {
                return false;
            }

            return MarkPublished(envelope.GlobalSequence);
        }

        return false;
    }

    public long Published => Volatile.Read(ref published);

    public KafkaCapacityLatencySnapshot CdcToKafkaLatency => cdcToKafkaLatency.Snapshot();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private bool MarkPublished(long globalSequence)
    {
        if (globalSequence < 0 || globalSequence >= publishedBits.Length * 64L)
        {
            return false;
        }

        var word = (int)(globalSequence >> 6);
        var mask = 1L << ((int)globalSequence & 63);
        lock (gate)
        {
            if ((committedBits[word] & mask) == 0)
            {
                return false;
            }

            if ((publishedBits[word] & mask) != 0)
            {
                return true;
            }

            publishedBits[word] |= mask;
        }

        Interlocked.Increment(ref published);
        return true;
    }
}
