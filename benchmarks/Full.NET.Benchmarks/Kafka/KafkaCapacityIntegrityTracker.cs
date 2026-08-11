using System.Numerics;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 使用固定容量位图跟踪 Ack、消费、重复、损坏和分区顺序。
/// </summary>
public sealed class KafkaCapacityIntegrityTracker
{
    private readonly int maximumMessages;
    private readonly long[] enqueuedBits;
    private readonly long[] acknowledgedBits;
    private readonly long[] consumedBits;
    private readonly long[] nextPartitionSequences;
    private readonly object[] partitionLocks;
    private long enqueued;
    private long acknowledged;
    private long consumed;
    private long duplicate;
    private long corrupted;
    private long outOfOrder;
    private long invalidSequence;

    /// <summary>
    /// 创建容量固定的完整性跟踪器，运行期间不按消息数量分配对象。
    /// </summary>
    public KafkaCapacityIntegrityTracker(
        int maximumMessages,
        int partitionCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumMessages);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(partitionCount);
        this.maximumMessages = maximumMessages;
        var wordCount = checked((maximumMessages + 63) / 64);
        enqueuedBits = new long[wordCount];
        acknowledgedBits = new long[wordCount];
        consumedBits = new long[wordCount];
        nextPartitionSequences = new long[partitionCount];
        partitionLocks = Enumerable.Range(0, partitionCount)
            .Select(static _ => new object())
            .ToArray();
    }

    /// <summary>
    /// 登记进入 Producer 有界队列的全局序号。
    /// </summary>
    public void OnEnqueued(long globalSequence)
    {
        var sequence = ValidateSequence(globalSequence);
        if (TrySet(enqueuedBits, sequence))
        {
            Interlocked.Increment(ref enqueued);
        }
    }

    /// <summary>
    /// 登记 Broker DeliveryReport 已确认的全局序号。
    /// </summary>
    public void OnAcknowledged(long globalSequence)
    {
        var sequence = ValidateSequence(globalSequence);
        if (!IsSet(enqueuedBits, sequence))
        {
            Interlocked.Increment(ref invalidSequence);
            throw new InvalidOperationException(
                "A Kafka acknowledgement cannot precede local enqueue registration.");
        }

        if (TrySet(acknowledgedBits, sequence))
        {
            Interlocked.Increment(ref acknowledged);
        }
    }

    /// <summary>
    /// 登记一次消费，并按实际 Partition 检查唯一性、Payload 和连续顺序。
    /// </summary>
    public void OnConsumed(
        long globalSequence,
        int partition,
        long partitionSequence,
        bool payloadValid)
    {
        var sequence = ValidateSequence(globalSequence);
        if (partition < 0 || partition >= partitionLocks.Length)
        {
            Interlocked.Increment(ref invalidSequence);
            throw new ArgumentOutOfRangeException(nameof(partition));
        }

        if (partitionSequence < 0)
        {
            Interlocked.Increment(ref invalidSequence);
            throw new ArgumentOutOfRangeException(nameof(partitionSequence));
        }

        if (!TrySet(consumedBits, sequence))
        {
            Interlocked.Increment(ref duplicate);
            return;
        }

        Interlocked.Increment(ref consumed);
        if (!payloadValid)
        {
            Interlocked.Increment(ref corrupted);
        }

        lock (partitionLocks[partition])
        {
            var expected = nextPartitionSequences[partition];
            if (partitionSequence != expected)
            {
                Interlocked.Increment(ref outOfOrder);
            }

            if (partitionSequence >= expected)
            {
                nextPartitionSequences[partition] = partitionSequence + 1;
            }
        }
    }

    /// <summary>
    /// 登记无法安全解码到全局序号的损坏消息。
    /// </summary>
    public void OnCorrupted() => Interlocked.Increment(ref corrupted);

    /// <summary>
    /// 冻结当前计数并计算 Ack 后丢失和 Producer 未 Flush 数量。
    /// </summary>
    public KafkaCapacityIntegrityEvidence Complete(bool drainCompleted)
    {
        long lost = 0;
        for (var index = 0; index < acknowledgedBits.Length; index++)
        {
            var acknowledgedWord = (ulong)Volatile.Read(
                ref acknowledgedBits[index]);
            var consumedWord = (ulong)Volatile.Read(ref consumedBits[index]);
            lost += BitOperations.PopCount(acknowledgedWord & ~consumedWord);
        }

        var enqueuedCount = Volatile.Read(ref enqueued);
        var acknowledgedCount = Volatile.Read(ref acknowledged);
        return new KafkaCapacityIntegrityEvidence(
            enqueuedCount,
            acknowledgedCount,
            Volatile.Read(ref consumed),
            lost,
            Volatile.Read(ref duplicate),
            Volatile.Read(ref corrupted),
            Volatile.Read(ref outOfOrder),
            Math.Max(0, enqueuedCount - acknowledgedCount),
            Volatile.Read(ref invalidSequence),
            drainCompleted);
    }

    private int ValidateSequence(long globalSequence)
    {
        if (globalSequence < 0 || globalSequence >= maximumMessages)
        {
            Interlocked.Increment(ref invalidSequence);
            throw new ArgumentOutOfRangeException(nameof(globalSequence));
        }

        return (int)globalSequence;
    }

    private static bool IsSet(long[] bits, int sequence)
    {
        var word = sequence >> 6;
        var mask = 1L << (sequence & 63);
        return (Volatile.Read(ref bits[word]) & mask) != 0;
    }

    private static bool TrySet(long[] bits, int sequence)
    {
        var word = sequence >> 6;
        var mask = 1L << (sequence & 63);
        while (true)
        {
            var current = Volatile.Read(ref bits[word]);
            if ((current & mask) != 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(
                    ref bits[word],
                    current | mask,
                    current) == current)
            {
                return true;
            }
        }
    }
}
