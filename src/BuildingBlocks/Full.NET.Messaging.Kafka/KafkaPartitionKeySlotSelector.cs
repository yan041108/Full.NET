using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 使用稳定的 xxHash64 把业务 Key 映射到固定槽位，确保同 Key 始终串行且映射不受进程随机种子影响。
/// </summary>
internal static class KafkaPartitionKeySlotSelector
{
    private const int MaximumKeyBytes = 256;
    private const ulong Prime1 = 11400714785074694791UL;
    private const ulong Prime2 = 14029467366897019727UL;
    private const ulong Prime3 = 1609587929392839161UL;
    private const ulong Prime4 = 9650029242287828579UL;
    private const ulong Prime5 = 2870177450012600261UL;

    public static int SelectSlot(string? key, int slotCount)
    {
        if (slotCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(slotCount));
        }

        if (string.IsNullOrEmpty(key) || Encoding.UTF8.GetByteCount(key) > MaximumKeyBytes)
        {
            return 0;
        }

        return (int)(ComputeHash(key) % (uint)slotCount);
    }

    internal static ulong ComputeHash(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount > MaximumKeyBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        Span<byte> bytes = stackalloc byte[MaximumKeyBytes];
        Encoding.UTF8.GetBytes(value, bytes);
        return ComputeHash(bytes[..byteCount]);
    }

    private static ulong ComputeHash(ReadOnlySpan<byte> bytes)
    {
        var remaining = bytes;
        ulong hash;
        if (remaining.Length >= 32)
        {
            var accumulator1 = unchecked(Prime1 + Prime2);
            var accumulator2 = Prime2;
            ulong accumulator3 = 0;
            var accumulator4 = unchecked(0UL - Prime1);
            do
            {
                accumulator1 = Round(accumulator1, BinaryPrimitives.ReadUInt64LittleEndian(remaining));
                accumulator2 = Round(accumulator2, BinaryPrimitives.ReadUInt64LittleEndian(remaining[8..]));
                accumulator3 = Round(accumulator3, BinaryPrimitives.ReadUInt64LittleEndian(remaining[16..]));
                accumulator4 = Round(accumulator4, BinaryPrimitives.ReadUInt64LittleEndian(remaining[24..]));
                remaining = remaining[32..];
            }
            while (remaining.Length >= 32);

            hash = BitOperations.RotateLeft(accumulator1, 1)
                   + BitOperations.RotateLeft(accumulator2, 7)
                   + BitOperations.RotateLeft(accumulator3, 12)
                   + BitOperations.RotateLeft(accumulator4, 18);
            hash = MergeRound(hash, accumulator1);
            hash = MergeRound(hash, accumulator2);
            hash = MergeRound(hash, accumulator3);
            hash = MergeRound(hash, accumulator4);
        }
        else
        {
            hash = Prime5;
        }

        hash += (uint)bytes.Length;
        while (remaining.Length >= 8)
        {
            var lane = Round(0, BinaryPrimitives.ReadUInt64LittleEndian(remaining));
            hash ^= lane;
            hash = unchecked((BitOperations.RotateLeft(hash, 27) * Prime1) + Prime4);
            remaining = remaining[8..];
        }

        if (remaining.Length >= 4)
        {
            hash ^= unchecked(BinaryPrimitives.ReadUInt32LittleEndian(remaining) * Prime1);
            hash = unchecked((BitOperations.RotateLeft(hash, 23) * Prime2) + Prime3);
            remaining = remaining[4..];
        }

        foreach (var value in remaining)
        {
            hash ^= value * Prime5;
            hash = unchecked(BitOperations.RotateLeft(hash, 11) * Prime1);
        }

        hash ^= hash >> 33;
        hash = unchecked(hash * Prime2);
        hash ^= hash >> 29;
        hash = unchecked(hash * Prime3);
        return hash ^ (hash >> 32);
    }

    private static ulong Round(ulong accumulator, ulong input)
    {
        accumulator = unchecked(accumulator + (input * Prime2));
        accumulator = BitOperations.RotateLeft(accumulator, 31);
        return unchecked(accumulator * Prime1);
    }

    private static ulong MergeRound(ulong accumulator, ulong value)
    {
        accumulator ^= Round(0, value);
        return unchecked((accumulator * Prime1) + Prime4);
    }
}
