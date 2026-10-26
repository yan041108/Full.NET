using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 回退控制面配置：仅在运维显式启用且完成 Connector/Topic 映射后替换失败关闭实现。
/// </summary>
public sealed class KafkaConnectRollbackOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "Messaging:KafkaConnectRollback";

    /// <summary>
    /// 是否启用 Kafka Connect 回退控制面；默认 false，仅在运维显式启用且完成 Connector/Topic 映射后生效。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Kafka Connect REST API 基地址，例如 http://connect:8083。</summary>
    public string? ConnectBaseUri { get; set; }

    /// <summary>准备阶段（Connector 暂停/复位）超时秒数；默认 120。</summary>
    public int PrepareTimeoutSeconds { get; set; } = 120;

    /// <summary>排空阶段（等待在途消息消费完毕）超时秒数；默认 120。</summary>
    public int DrainTimeoutSeconds { get; set; } = 120;

    /// <summary>排空阶段轮询 Connector 状态的间隔毫秒数；默认 1000。</summary>
    public int DrainPollIntervalMilliseconds { get; set; } = 1_000;

    /// <summary>事件流与 Kafka Connect / Consumer 资源的稳定绑定列表。</summary>
    public KafkaConnectRollbackStreamBinding[] Streams { get; set; } = [];
}

/// <summary>启用控制面时提前拒绝不完整映射、无界等待及重复资源。</summary>
internal sealed class KafkaConnectRollbackOptionsValidator : IValidateOptions<KafkaConnectRollbackOptions>
{
    public ValidateOptionsResult Validate(string? name, KafkaConnectRollbackOptions options)
    {
        if (!options.Enabled) return ValidateOptionsResult.Success;
        if (!Uri.TryCreate(options.ConnectBaseUri, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            return ValidateOptionsResult.Fail("Messaging:KafkaConnectRollback:ConnectBaseUri 必须是无凭据、路径或查询的 HTTP(S) 服务地址。");
        if (options.PrepareTimeoutSeconds is < 1 or > 300 || options.DrainTimeoutSeconds is < 1 or > 300
            || options.DrainPollIntervalMilliseconds is < 10 or > 10000)
            return ValidateOptionsResult.Fail("回退超时必须在 1..300 秒，轮询间隔必须在 10..10000 毫秒。");
        if (options.Streams.Length is < 1 or > 64 || options.Streams.Any(item =>
                !MessagingNames.MessageTypePattern.IsMatch(item.EventType) || item.SchemaVersion <= 0
                || !MessagingNames.TopicCodePattern.IsMatch(item.TopicName)
                || !MessagingNames.ConsumerNamePattern.IsMatch(item.ConsumerGroupId)
                || string.IsNullOrWhiteSpace(item.ConnectorName) || item.ConnectorName.Length > 128
                || item.ConnectorName.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.')))
            || options.Streams.Any(item => (!string.IsNullOrEmpty(item.ShadowTopicName) || !string.IsNullOrEmpty(item.ShadowConnectorName))
                && (!MessagingNames.TopicCodePattern.IsMatch(item.ShadowTopicName) || item.ShadowTopicName == item.TopicName
                    || string.IsNullOrWhiteSpace(item.ShadowConnectorName) || item.ShadowConnectorName == item.ConnectorName || item.ShadowConnectorName.Length > 128
                    || item.ShadowConnectorName.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.'))))
            || options.Streams.Select(item => (item.EventType, item.SchemaVersion)).Distinct().Count() != options.Streams.Length)
            return ValidateOptionsResult.Fail("回退映射必须完整、有效且事件流不可重复。");
        return ValidateOptionsResult.Success;
    }
}

/// <summary>事件流与 Kafka Connect / Consumer 资源的稳定绑定。</summary>
public sealed class KafkaConnectRollbackStreamBinding
{
    /// <summary>事件类型名称，对应领域事件的契约类型标识。</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>事件契约 Schema 版本号。</summary>
    public int SchemaVersion { get; set; }

    /// <summary>对应 Kafka Connect Connector 的名称。</summary>
    public string ConnectorName { get; set; } = string.Empty;

    /// <summary>对应 Kafka Topic 名称。</summary>
    public string TopicName { get; set; } = string.Empty;

    /// <summary>专用影子 Topic，验证只读此 Topic，不与正式业务 Topic 混用。</summary>
    public string ShadowTopicName { get; set; } = string.Empty;

    /// <summary>仅捕获独立影子表的专用 Connector；不能与正式 Connector 同名。</summary>
    public string ShadowConnectorName { get; set; } = string.Empty;

    /// <summary>当前部署指定的源库名，必须与实际 Connector 配置一致后才允许建立切流证明。</summary>
    public string SourceDatabaseName { get; set; } = string.Empty;

    /// <summary>目标 Worker 的 Kafka 客户端前缀；留空时采用当前 Kafka 配置前缀。</summary>
    public string WorkerClientId { get; set; } = string.Empty;

    /// <summary>对应 Consumer Group 标识。</summary>
    public string ConsumerGroupId { get; set; } = string.Empty;
}
