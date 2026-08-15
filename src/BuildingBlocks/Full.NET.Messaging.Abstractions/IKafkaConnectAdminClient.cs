namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// Kafka Connect REST 管理客户端；用于集成测试、容量 Runner 与回退控制面。
/// </summary>
public interface IKafkaConnectAdminClient : IDisposable
{
    Task<bool> WaitUntilReadyAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    Task RegisterConnectorAsync(
        string connectorName,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default);

    Task<bool> WaitForConnectorHealthyAsync(
        string connectorName,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task DeleteConnectorAsync(string connectorName, CancellationToken cancellationToken = default);

    Task PauseConnectorAsync(string connectorName, CancellationToken cancellationToken = default);

    Task ResumeConnectorAsync(string connectorName, CancellationToken cancellationToken = default);

    Task<bool> IsConnectorPausedAsync(string connectorName, CancellationToken cancellationToken = default);

    Task<CdcDeliveryPosition?> TryReadConnectorPositionAsync(
        string connectorName,
        CancellationToken cancellationToken = default);

    Task<string?> TryGetConnectorStatusAsync(
        string connectorName,
        CancellationToken cancellationToken = default);
}
