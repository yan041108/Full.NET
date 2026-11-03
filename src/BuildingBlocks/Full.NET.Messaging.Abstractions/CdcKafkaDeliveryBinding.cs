using System.Text.Json;

namespace Full.NET.Messaging.Abstractions;

/// <summary>切流时持久化的稳定交付资源身份；不包含凭据、动态 Worker 成员或伪造源检查点。</summary>
public sealed record CdcKafkaDeliveryBinding(string KafkaClusterId, string ConnectorName, string ConfigurationFingerprint,
    string TopicName, string[] ConsumerGroups, string[] RetryStages, CdcKafkaTopicBinding[] Topics)
{
    /// <summary>每次成功切流生成新的 UUID v7，防止相同拓扑再次激活时认领旧代次。</summary>
    public Guid ActivationId { get; init; }
    /// <summary>切流时确认的控制端点；纠偏不能根据后改配置选择另一 Connect。</summary>
    public string? ConnectBaseUri { get; init; }
    /// <summary>切流时所属环境；禁止跨环境自动重放控制目标。</summary>
    public string? EnvironmentName { get; init; }
    /// <summary>切流证明所用的专用影子Connector；多流隔离禁止与另一流的正式或影子发布者交叉复用。</summary>
    public string? ShadowConnectorName { get; init; }
    /// <summary>切流证明所用的专用影子Topic；不能与另一流的正式、重试或影子Topic重叠。</summary>
    public string? ShadowTopicName { get; init; }
    /// <summary>通过静态源生成闭包保存原始基线。</summary>
    public string ToJson() => JsonSerializer.Serialize(this, MessagingJsonSerializerContext.Default.CdcKafkaDeliveryBinding);

    /// <summary>确认同一环境和控制集群中的两条活动流独占正式/影子Connector及全部Topic；消费组可共享。</summary>
    /// <param name="other">另一条流已持久化且验证过的交付基线。</param>
    /// <returns>只有双方资源完整且没有发布/排空资源重叠时返回true。</returns>
    public bool IsIsolatedFrom(CdcKafkaDeliveryBinding other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (string.IsNullOrWhiteSpace(ShadowConnectorName) || string.IsNullOrWhiteSpace(other.ShadowConnectorName)
            || string.IsNullOrWhiteSpace(ShadowTopicName) || string.IsNullOrWhiteSpace(other.ShadowTopicName)) return false;
        return ActivationId != Guid.Empty && other.ActivationId != Guid.Empty
            && !string.IsNullOrWhiteSpace(ConnectBaseUri) && ConnectBaseUri == other.ConnectBaseUri
            && !string.IsNullOrWhiteSpace(EnvironmentName) && EnvironmentName == other.EnvironmentName
            && KafkaClusterId == other.KafkaClusterId
            && !new[] { ConnectorName, ShadowConnectorName }.Any(name => name == other.ConnectorName || name == other.ShadowConnectorName)
            && Topics.Length > 0 && other.Topics.Length > 0
            && !Topics.Select(topic => topic.TopicName).Append(ShadowTopicName).Any(name => name == other.ShadowTopicName || other.Topics.Any(topic => topic.TopicName == name))
            && !Topics.Any(topic => other.Topics.Any(candidate => candidate.TopicName == topic.TopicName || candidate.TopicId == topic.TopicId));
    }

    /// <summary>解析持久基线；旧记录缺失或不完整时失败关闭，不从当前 Broker 猜测。</summary>
    public static bool TryParse(string? json, out CdcKafkaDeliveryBinding? binding)
    {
        binding = null;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            binding = JsonSerializer.Deserialize(json, MessagingJsonSerializerContext.Default.CdcKafkaDeliveryBinding);
            return binding is not null && !string.IsNullOrWhiteSpace(binding.KafkaClusterId) && !string.IsNullOrWhiteSpace(binding.ConnectorName)
                && !string.IsNullOrWhiteSpace(binding.ConfigurationFingerprint) && !string.IsNullOrWhiteSpace(binding.TopicName)
                && binding.ConsumerGroups is { Length: > 0 } && binding.RetryStages is not null && binding.Topics is { Length: > 0 and <= 32 }
                && binding.Topics.All(t => t is not null && !string.IsNullOrWhiteSpace(t.TopicId) && !string.IsNullOrWhiteSpace(t.TopicName)
                    && t.Partitions is { Length: > 0 and <= 256 } && t.Partitions.All(p => p is not null && p.Partition >= 0 && p.Low >= 0 && p.High >= p.Low));
        }
        catch (JsonException) { return false; }
    }
}

/// <summary>不可变 Topic ID 与切流时的真实分区窗口。</summary>
public sealed record CdcKafkaTopicBinding(string TopicName, string TopicId, CdcKafkaPartitionBinding[] Partitions);

/// <summary>分区身份及观测时实际保留起点、排他高水位；不将 offset 差当作业务确认。</summary>
public sealed record CdcKafkaPartitionBinding(int Partition, long Low, long High);
