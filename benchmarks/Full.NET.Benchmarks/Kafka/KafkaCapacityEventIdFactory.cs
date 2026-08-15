using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 为容量 Outbox 与 CDC 跟踪生成可复现的 EventId。
/// </summary>
public static class KafkaCapacityEventIdFactory
{
    public static Guid Create(uint runHash, uint sampleHash, long globalSequence)
    {
        if (globalSequence is < 0 or > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(globalSequence));
        }

        var payload = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{runHash:x8}|{sampleHash:x8}|{globalSequence}");
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(payload), hash);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x70);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash[..16]);
    }
}
