using Full.NET.Abstractions.Messaging;
using Full.NET.Messaging.Abstractions;
using System.Diagnostics;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 定义 Scope B 专用的稳定业务事件和 Consumer 标识。
/// </summary>
public static class KafkaCapacityWorkerContracts
{
    public const string EventType = "fullnet.capacity.worker.message";

    public const int SchemaVersion = 1;

    public const string ConsumerName = "fullnet.capacity.worker";

    public const string TopicCode = "capacity.worker.v1";
}

/// <summary>
/// 保存 Scope B Handler 端可验证的处理、重复和损坏计数。
/// </summary>
public sealed class KafkaCapacityWorkerObserver
{
    private readonly long[] processedBits;
    private KafkaCapacityLatencyHistogram endToEndLatency = new();
    private uint expectedRunHash;
    private uint expectedSampleHash;
    private long processed;
    private long corrupted;
    private int identityConfigured;

    public KafkaCapacityWorkerObserver(int maximumMessages)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumMessages);
        processedBits = new long[checked((maximumMessages + 63) / 64)];
    }

    public void BeginPhase(uint runHash, uint sampleHash)
    {
        Array.Clear(processedBits);
        Volatile.Write(ref processed, 0);
        Volatile.Write(ref corrupted, 0);
        endToEndLatency = new KafkaCapacityLatencyHistogram();
        expectedRunHash = runHash;
        expectedSampleHash = sampleHash;
        Volatile.Write(ref identityConfigured, 1);
    }

    public bool OnHandled(ReadOnlySpan<byte> payload)
    {
        if (!KafkaCapacityEnvelopeCodec.TryDecode(payload, out var envelope)
            || (Volatile.Read(ref identityConfigured) != 0
                && (envelope.RunHash != expectedRunHash
                    || envelope.SampleHash != expectedSampleHash))
            || envelope.GlobalSequence < 0
            || envelope.GlobalSequence >= processedBits.Length * 64L)
        {
            Interlocked.Increment(ref corrupted);
            return false;
        }

        var sequence = checked((int)envelope.GlobalSequence);
        var word = sequence >> 6;
        var mask = 1L << (sequence & 63);
        while (true)
        {
            var current = Volatile.Read(ref processedBits[word]);
            if ((current & mask) != 0)
            {
                return true;
            }

            if (Interlocked.CompareExchange(
                    ref processedBits[word],
                    current | mask,
                    current) == current)
            {
                Interlocked.Increment(ref processed);
                endToEndLatency.RecordMicroseconds(Math.Max(
                    1,
                    CurrentTimestampMicroseconds() - envelope.ScheduledTimestamp));
                return true;
            }
        }
    }

    public KafkaCapacityWorkerSnapshot Snapshot() =>
        new(
            Volatile.Read(ref processed),
            Volatile.Read(ref corrupted),
            endToEndLatency.Snapshot());

    private static long CurrentTimestampMicroseconds() =>
        Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp()).Ticks / 10;
}

public sealed record KafkaCapacityWorkerSnapshot(
    long Processed,
    long Corrupted,
    KafkaCapacityLatencySnapshot EndToEndLatency);

/// <summary>
/// 作为真实 Dispatcher 目标记录 Scope B Handler 已成功完成的业务负载。
/// </summary>
public sealed class KafkaCapacityWorkerSubscription(
    KafkaCapacityWorkerObserver observer) : IIntegrationEventSubscription
{
    public string ConsumerName => KafkaCapacityWorkerContracts.ConsumerName;

    public string EventType => KafkaCapacityWorkerContracts.EventType;

    public int SchemaVersion => KafkaCapacityWorkerContracts.SchemaVersion;

    public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
        IntegrationEventIdempotencyStrategy.MessageIdDeduplication;

    public Task HandleAsync(
        IntegrationEventContext context,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!observer.OnHandled(payload.Span))
        {
            throw new InvalidDataException(
                "Scope B handler rejected an invalid capacity payload.");
        }

        return Task.CompletedTask;
    }
}
