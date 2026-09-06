namespace Full.NET.Modules.Mqtt.Configuration;

/// <summary>MQTT Broker 连接与受控发布限额配置。</summary>
public sealed class MqttBrokerOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "FullNet:Mqtt:Broker";

    /// <summary>是否启用 MQTT 发布能力。</summary>
    public bool Enabled { get; init; }

    /// <summary>Broker 主机名。</summary>
    public string Host { get; init; } = "localhost";

    /// <summary>Broker 端口。</summary>
    public int Port { get; init; } = 1883;

    /// <summary>是否使用 TLS。</summary>
    public bool UseTls { get; init; }

    /// <summary>可选用户名。</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>可选密码；不得通过健康 API 回显。</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>单条消息最大载荷字节数。</summary>
    public int MaximumPayloadBytes { get; init; } = 64 * 1024;

    /// <summary>单用户每分钟最大发布次数。</summary>
    public int MaximumPublishRatePerMinute { get; init; } = 60;

    /// <summary>允许发布的主题前缀白名单。</summary>
    public IReadOnlyList<string> AllowedPublishTopicPrefixes { get; init; } =
        ["fullnet/", "tenants/"];

    /// <summary>部署说明。</summary>
    public string DeploymentNotice { get; init; } =
        "MQTT 控制面提供受 ACL、载荷大小、速率与幂等键约束的首个发布场景；Kafka 仍仅用于内部 Integration Event 交付，两者语义不可互换。";
}
