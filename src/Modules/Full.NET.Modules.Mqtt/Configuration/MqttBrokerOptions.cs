namespace Full.NET.Modules.Mqtt.Configuration;

/// <summary>MQTT Broker 连接与受控发布限额配置。</summary>
/// <remarks>绑定属性必须可写，使配置源生成器应用部署连接、限额与主题前缀配置。</remarks>
public sealed class MqttBrokerOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "FullNet:Mqtt:Broker";

    /// <summary>是否启用 MQTT 发布能力。</summary>
    public bool Enabled { get; set; }

    /// <summary>Broker 主机名。</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>Broker 端口。</summary>
    public int Port { get; set; } = 1883;

    /// <summary>是否使用 TLS。</summary>
    public bool UseTls { get; set; }

    /// <summary>可选用户名。</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>可选密码；不得通过健康 API 回显。</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>单条消息最大载荷字节数。</summary>
    public int MaximumPayloadBytes { get; set; } = 64 * 1024;

    /// <summary>单用户每分钟最大发布次数。</summary>
    public int MaximumPublishRatePerMinute { get; set; } = 60;

    /// <summary>允许发布的主题前缀白名单。</summary>
    public IReadOnlyList<string> AllowedPublishTopicPrefixes { get; set; } =
        ["fullnet/", "tenants/"];

    /// <summary>部署说明。</summary>
    public string DeploymentNotice { get; set; } =
        "MQTT 控制面提供受 ACL、载荷大小、速率与幂等键约束的首个发布场景；Kafka 仍仅用于内部 Integration Event 交付，两者语义不可互换。";
}
