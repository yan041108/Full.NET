using Microsoft.Extensions.Options;

namespace Full.NET.Messaging.Kafka;

/// <summary>Worker 的有界只读 CDC 观测；默认关闭，不启用切流或任何恢复动作。</summary>
public sealed class KafkaCdcMonitoringOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "Messaging:CdcMonitoring";
    /// <summary>显式启用数据库、Connect 与目录消费组观测，需与真实资源映射一起部署。</summary>
    public bool Enabled { get; set; }
    /// <summary>采集结束到下一轮的间隔秒数，允许 10～300，默认 30。</summary>
    public int PollSeconds { get; set; } = 30;
    /// <summary>单轮数据库与 Connect 采集期限秒数，允许 5～60，默认 10；Kafka SDK 单请求另有 10 秒上限。</summary>
    public int SampleTimeoutSeconds { get; set; } = 10;
    /// <summary>样本最大年龄秒数，默认 120；必须覆盖轮询间隔和采集期限，过期样本不提供健康数值。</summary>
    public int MaximumSampleAgeSeconds { get; set; } = 120;
    /// <summary>单流便捷配置：显式确认自创建起使用LogAppendTime的Topic ID；多流必须使用RetentionTimeTopics，重建后重新确认。</summary>
    public string RetentionTimeTopicId { get; set; } = string.Empty;
    /// <summary>按目录Topic稳定码确认其LogAppendTime身份，最多64条；未确认的流只观测消息数，不推断时间保留比。</summary>
    public Dictionary<string, string> RetentionTimeTopics { get; set; } = new(StringComparer.Ordinal);
    /// <summary>部署侧保证 Broker 和采集器各自相对真实 UTC 的绝对误差秒数，0～30，默认 5；窗口风险加入四倍保守预算。</summary>
    public int MaximumBrokerClockSkewSeconds { get; set; } = 5;
}

internal sealed class KafkaCdcMonitoringOptionsValidator : IValidateOptions<KafkaCdcMonitoringOptions>
{
    public ValidateOptionsResult Validate(string? name, KafkaCdcMonitoringOptions options) =>
        options.PollSeconds is < 10 or > 300 || options.SampleTimeoutSeconds is < 5 or > 60
        || options.MaximumSampleAgeSeconds < options.PollSeconds + options.SampleTimeoutSeconds
        || options.MaximumSampleAgeSeconds > 900
        || options.RetentionTimeTopicId?.Length > 64 || options.MaximumBrokerClockSkewSeconds is < 0 or > 30
        || options.RetentionTimeTopics is null || options.RetentionTimeTopics.Count > 64
        || options.RetentionTimeTopics.Any(item => !Full.NET.Messaging.Abstractions.MessagingNames.TopicCodePattern.IsMatch(item.Key)
            || string.IsNullOrWhiteSpace(item.Value) || item.Value.Length > 64)
        || (!string.IsNullOrWhiteSpace(options.RetentionTimeTopicId) && options.RetentionTimeTopics.Count > 0)
            ? ValidateOptionsResult.Fail("CDC 观测轮询、期限或新鲜度超出有界范围。") : ValidateOptionsResult.Success;
}
