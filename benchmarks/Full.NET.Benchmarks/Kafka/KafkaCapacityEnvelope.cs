using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 表示从固定二进制测试信封解码出的测量字段。
/// </summary>
public sealed record KafkaCapacityEnvelope(
    uint RunHash,
    uint SampleHash,
    long GlobalSequence,
    long PartitionSequence,
    long ScheduledTimestamp,
    long EnqueuedTimestamp,
    int PayloadSizeBytes);

/// <summary>
/// 编解码可确定重建并由完整 SHA-256 摘要保护的容量测试信封。
/// </summary>
public static class KafkaCapacityEnvelopeCodec
{
    public const int MinimumPayloadSizeBytes = 64;
    public const int MaximumPayloadSizeBytes = 1_048_576;

    private const ushort Magic = 0x4E46;
    private const int Version = 1;
    private const int HeaderSizeBytes = 32;
    private const int HashSizeBytes = 32;
    private const int MaximumEnqueueDelay = 0x00FF_FFFF;

    /// <summary>
    /// 编码指定总长度的测试消息值；时间戳必须使用同一单调时钟单位。
    /// </summary>
    public static byte[] Encode(
        int payloadSizeBytes,
        uint runHash,
        uint sampleHash,
        long globalSequence,
        long partitionSequence,
        long scheduledTimestamp,
        long enqueuedTimestamp)
    {
        if (payloadSizeBytes is < MinimumPayloadSizeBytes
            or > MaximumPayloadSizeBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadSizeBytes));
        }

        if (globalSequence is < 0 or > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(globalSequence));
        }

        if (partitionSequence is < 0 or > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(partitionSequence));
        }

        if (scheduledTimestamp < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scheduledTimestamp));
        }

        if (enqueuedTimestamp < scheduledTimestamp)
        {
            throw new ArgumentOutOfRangeException(nameof(enqueuedTimestamp));
        }

        var enqueueDelay = enqueuedTimestamp - scheduledTimestamp;
        if (enqueueDelay > MaximumEnqueueDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(enqueuedTimestamp));
        }

        var buffer = new byte[payloadSizeBytes];
        var span = buffer.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(span, Magic);
        WriteUInt24(
            span[2..],
            ((payloadSizeBytes - MinimumPayloadSizeBytes) << 4) | Version);
        BinaryPrimitives.WriteUInt32LittleEndian(span[5..], runHash);
        BinaryPrimitives.WriteUInt32LittleEndian(span[9..], sampleHash);
        BinaryPrimitives.WriteUInt32LittleEndian(span[13..], (uint)globalSequence);
        BinaryPrimitives.WriteUInt32LittleEndian(span[17..], (uint)partitionSequence);
        BinaryPrimitives.WriteInt64LittleEndian(span[21..], scheduledTimestamp);
        WriteUInt24(span[29..], (int)enqueueDelay);

        FillDeterministicPayload(
            span[HeaderSizeBytes..^HashSizeBytes],
            runHash,
            sampleHash,
            (uint)globalSequence,
            (uint)partitionSequence);
        SHA256.HashData(
            span[..^HashSizeBytes],
            span[^HashSizeBytes..]);
        return buffer;
    }

    /// <summary>
    /// 校验版本、声明长度和 SHA-256 后解码信封；任何不一致均失败关闭。
    /// </summary>
    public static bool TryDecode(
        ReadOnlySpan<byte> value,
        out KafkaCapacityEnvelope envelope)
    {
        envelope = default!;
        if (value.Length is < MinimumPayloadSizeBytes
            or > MaximumPayloadSizeBytes
            || BinaryPrimitives.ReadUInt16LittleEndian(value) != Magic)
        {
            return false;
        }

        var metadata = ReadUInt24(value[2..]);
        var version = metadata & 0x0F;
        var encodedLength = (metadata >> 4) + MinimumPayloadSizeBytes;
        if (version != Version || encodedLength != value.Length)
        {
            return false;
        }

        Span<byte> calculatedHash = stackalloc byte[HashSizeBytes];
        SHA256.HashData(value[..^HashSizeBytes], calculatedHash);
        if (!CryptographicOperations.FixedTimeEquals(
                calculatedHash,
                value[^HashSizeBytes..]))
        {
            return false;
        }

        var scheduledTimestamp = BinaryPrimitives.ReadInt64LittleEndian(value[21..]);
        var enqueueDelay = ReadUInt24(value[29..]);
        if (scheduledTimestamp < 0
            || scheduledTimestamp > long.MaxValue - enqueueDelay)
        {
            return false;
        }

        envelope = new KafkaCapacityEnvelope(
            BinaryPrimitives.ReadUInt32LittleEndian(value[5..]),
            BinaryPrimitives.ReadUInt32LittleEndian(value[9..]),
            BinaryPrimitives.ReadUInt32LittleEndian(value[13..]),
            BinaryPrimitives.ReadUInt32LittleEndian(value[17..]),
            scheduledTimestamp,
            scheduledTimestamp + enqueueDelay,
            value.Length);
        return true;
    }

    private static void FillDeterministicPayload(
        Span<byte> payload,
        uint runHash,
        uint sampleHash,
        uint globalSequence,
        uint partitionSequence)
    {
        var state = ((ulong)runHash << 32) | sampleHash;
        state ^= ((ulong)globalSequence << 32) | partitionSequence;
        for (var index = 0; index < payload.Length; index++)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            payload[index] = (byte)state;
        }
    }

    private static int ReadUInt24(ReadOnlySpan<byte> value) =>
        value[0] | (value[1] << 8) | (value[2] << 16);

    private static void WriteUInt24(Span<byte> destination, int value)
    {
        destination[0] = (byte)value;
        destination[1] = (byte)(value >> 8);
        destination[2] = (byte)(value >> 16);
    }
}
