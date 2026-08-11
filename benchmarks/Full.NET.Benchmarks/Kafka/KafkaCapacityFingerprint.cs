using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 生成报告和所有权比较使用的不可逆稳定 SHA-256 摘要。
/// </summary>
public static class KafkaCapacityFingerprint
{
    public static string Sha256(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
