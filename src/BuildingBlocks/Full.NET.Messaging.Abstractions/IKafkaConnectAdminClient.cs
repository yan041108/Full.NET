namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// Kafka Connect REST 管理客户端；用于集成测试、容量 Runner 与回退控制面。
/// </summary>
public interface IKafkaConnectAdminClient : IDisposable
{
    /// <summary>
    /// 等待 Connect REST 端点进入可服务状态；失败或超时返回 false。
    /// </summary>
    /// <param name="timeout">最大等待时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<bool> WaitUntilReadyAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// 注册或更新一个 Kafka Connect Connector 配置；已存在同名 Connector 时被覆盖。
    /// </summary>
    /// <param name="connectorName">Connector 稳定名称。</param>
    /// <param name="config">完整配置字典，含 connector.class、tasks.max 等必填项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RegisterConnectorAsync(
        string connectorName,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 轮询指定 Connector 直到所有 Task 进入 RUNNING 状态；超时或状态异常返回 false。
    /// </summary>
    /// <param name="connectorName">目标 Connector 名称。</param>
    /// <param name="timeout">最长等待窗口。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<bool> WaitForConnectorHealthyAsync(
        string connectorName,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除指定 Connector；Connector 不存在时静默成功。
    /// </summary>
    /// <param name="connectorName">目标 Connector 名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task DeleteConnectorAsync(string connectorName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 暂停指定 Connector，停止其 Task 拉取；已暂停时重复调用幂等。
    /// </summary>
    /// <param name="connectorName">目标 Connector 名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task PauseConnectorAsync(string connectorName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 恢复已暂停的 Connector；未暂停时重复调用幂等。
    /// </summary>
    /// <param name="connectorName">目标 Connector 名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task ResumeConnectorAsync(string connectorName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询 Connector 当前是否处于 PAUSED 状态；Connector 不存在返回 false。
    /// </summary>
    /// <param name="connectorName">目标 Connector 名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<bool> IsConnectorPausedAsync(string connectorName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取 Connector 当前已提交的源位点快照；Connector 不存在或无位点返回 null。
    /// </summary>
    /// <param name="connectorName">目标 Connector 名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<CdcDeliveryPosition?> TryReadConnectorPositionAsync(
        string connectorName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取 Connector 状态 JSON 原始字符串；用于运维诊断，Connector 不存在返回 null。
    /// </summary>
    /// <param name="connectorName">目标 Connector 名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<string?> TryGetConnectorStatusAsync(
        string connectorName,
        CancellationToken cancellationToken = default);
}
