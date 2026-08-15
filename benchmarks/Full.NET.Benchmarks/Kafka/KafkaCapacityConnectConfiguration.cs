namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 保存 Scope C 访问外部 Kafka Connect REST 控制面所需的受控配置。
/// </summary>
public sealed class KafkaCapacityConnectConfiguration
{
    /// <summary>
    /// 获取或设置 Connect REST 基址；Scope C 执行前必须非空。
    /// </summary>
    public string? BaseUri { get; set; }

    /// <summary>
    /// 获取或设置 Connect REST 请求超时秒数。
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// 获取或设置 Connector 注册后等待 RUNNING 的最长秒数。
    /// </summary>
    public int HealthTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// 获取或设置容量 Connector 名称前缀；实际名称追加 run 摘要后缀。
    /// </summary>
    public string ConnectorNamePrefix { get; set; } = "fullnet-capacity";

    /// <summary>
    /// 获取或设置 Connect 容器访问宿主机数据库时使用的网关主机名。
    /// </summary>
    public string DatabaseHostGateway { get; set; } = "host.docker.internal";

    /// <summary>
    /// 获取或设置 Connect 容器内访问 Kafka 的 bootstrap servers。
    /// </summary>
    public string? InternalKafkaBootstrapServers { get; set; }

    /// <summary>
    /// 获取或设置 MySQL Connector 专用账户；未配置时回退到 Database 连接串账户。
    /// </summary>
    public string? MySqlConnectorUser { get; set; }

    /// <summary>
    /// 获取或设置 MySQL Connector 专用口令；未配置时回退到 Database 连接串口令。
    /// </summary>
    public string? MySqlConnectorPassword { get; set; }
}
