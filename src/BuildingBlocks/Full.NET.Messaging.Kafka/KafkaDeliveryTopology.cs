using Full.NET.Messaging.Abstractions;

namespace Full.NET.Messaging.Kafka;

/// <summary>切流基线与后续实时资源比较；同名重建、分区改变或高水位倒退均失败关闭。</summary>
internal static class KafkaDeliveryTopology
{
    internal static bool Matches(CdcKafkaTopicBinding[] expected, CdcKafkaTopicBinding[]? current)
    {
        if (expected.Length == 0 || current is null || current.Length != expected.Length
            || expected.Select(t => t.TopicName).Distinct(StringComparer.Ordinal).Count() != expected.Length
            || current.Select(t => t.TopicName).Distinct(StringComparer.Ordinal).Count() != current.Length) return false;
        foreach (var prior in expected)
        {
            var actual = current.SingleOrDefault(t => t.TopicName == prior.TopicName);
            if (actual is null || string.IsNullOrWhiteSpace(prior.TopicId) || actual.TopicId != prior.TopicId
                || prior.Partitions.Length == 0 || actual.Partitions.Length != prior.Partitions.Length
                || prior.Partitions.Select(p => p.Partition).Distinct().Count() != prior.Partitions.Length
                || actual.Partitions.Select(p => p.Partition).Distinct().Count() != actual.Partitions.Length) return false;
            foreach (var p in prior.Partitions)
            {
                var partition = actual.Partitions.SingleOrDefault(item => item.Partition == p.Partition);
                if (partition is null || p.Low < 0 || p.High < p.Low || partition.Low < 0 || partition.High < partition.Low
                    || partition.Low != p.Low || partition.High < p.High) return false;
            }
        }
        return true;
    }
}
