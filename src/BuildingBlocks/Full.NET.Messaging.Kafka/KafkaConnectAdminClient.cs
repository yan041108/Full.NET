using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka.Serialization;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 记录同一回退 generation 的控制面 fence，供 <see cref="KafkaConnectEventDeliveryRollbackReadinessReader.AbortAsync"/> 幂等恢复。
/// </summary>
internal sealed class RollbackControlPlaneFenceRegistry
{
    private readonly ConcurrentDictionary<RollbackFenceKey, RollbackFenceState> _fences = new();

    public bool TryRegister(RollbackFenceKey key, RollbackFenceState state) =>
        _fences.TryAdd(key, state);

    public bool TryGet(RollbackFenceKey key, out RollbackFenceState state) =>
        _fences.TryGetValue(key, out state!);

    public bool TryRemove(RollbackFenceKey key) =>
        _fences.TryRemove(key, out _);
}

internal readonly record struct RollbackFenceKey(
    string EventType,
    int SchemaVersion,
    Guid RollbackGeneration);

internal sealed record RollbackFenceState(
    string ConnectorName,
    string ControlPlaneFenceToken,
    bool ConnectorPaused);

/// <summary>
/// Debezium Kafka Connect REST 客户端；生产回退、集成测试与容量 Runner 共用实现。
/// </summary>
public sealed class KafkaConnectAdminClient : IKafkaConnectAdminClient
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    /// <summary>使用外部注入的 HttpClient 构造 Connect REST 客户端，不接管其生命周期。</summary>
    public KafkaConnectAdminClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _ownsHttpClient = false;
    }

    /// <summary>基于 Connect REST 基地址构造 HttpClient 并接管其生命周期；默认 60 秒超时。</summary>
    public KafkaConnectAdminClient(Uri connectBaseUri, TimeSpan? timeout = null)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = connectBaseUri,
            Timeout = timeout ?? TimeSpan.FromSeconds(60),
        };
        _ownsHttpClient = true;
    }

    /// <summary>
    /// 轮询 Connect REST 根端点（GET /）直至返回成功或超时，用于等待 Connect 集群就绪。
    /// </summary>
    public async Task<bool> WaitUntilReadyAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await _httpClient
                    .GetAsync("/", cancellationToken)
                    .ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>
    /// 向 Connect REST 端点 POST /connectors 提交连接器注册；
    /// 失败时抛出 InvalidOperationException，错误体不回显以避免泄露数据库口令。
    /// </summary>
    public async Task RegisterConnectorAsync(
        string connectorName,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorName);
        ArgumentNullException.ThrowIfNull(config);
        var payload = new ConnectorRegistration(connectorName, config);
        using var response = await _httpClient
            .PostAsJsonAsync(
                "/connectors",
                payload,
                KafkaMessagingJsonSerializerContext.Default.ConnectorRegistration,
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // Kafka Connect 可能在错误体中回显提交的连接器配置，其中包含数据库口令。
            throw new InvalidOperationException(
                $"Connector registration failed with HTTP status {(int)response.StatusCode}.");
        }
    }

    /// <summary>
    /// 轮询连接器状态直至全部任务 RUNNING 或任一任务 FAILED/超时，
    /// 用于等待 Debezium 连接器完成启动。
    /// </summary>
    public async Task<bool> WaitForConnectorHealthyAsync(
        string connectorName,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var health = await TryGetConnectorHealthAsync(connectorName, cancellationToken)
                .ConfigureAwait(false);
            if (health == ConnectorHealth.Running)
            {
                return true;
            }

            if (health == ConnectorHealth.Failed)
            {
                return false;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>
    /// 向 Connect REST 端点 DELETE /connectors/{name} 删除连接器；
    /// 404 视为已删除，其余失败抛出。
    /// </summary>
    public async Task DeleteConnectorAsync(
        string connectorName,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .DeleteAsync($"/connectors/{connectorName}", cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    /// <summary>向 Connect REST 端点 PUT /connectors/{name}/pause 暂停连接器及其任务。</summary>
    public async Task PauseConnectorAsync(string connectorName, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .PutAsync($"/connectors/{connectorName}/pause", null, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>向 Connect REST 端点 PUT /connectors/{name}/resume 恢复已暂停的连接器及其任务。</summary>
    public async Task ResumeConnectorAsync(string connectorName, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .PutAsync($"/connectors/{connectorName}/resume", null, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// 检查 Connector 及其全部任务是否均已完成暂停。
    /// </summary>
    /// <param name="connectorName">Connector 名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>Connector 与至少一个任务均处于 PAUSED 时返回 <see langword="true"/>。</returns>
    public async Task<bool> IsConnectorPausedAsync(
        string connectorName,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync($"/connectors/{connectorName}/status", cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        var statusJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(statusJson);
        var root = document.RootElement;
        if (!root.TryGetProperty("connector", out var connector)
            || !connector.TryGetProperty("state", out var stateElement))
        {
            return false;
        }

        if (!string.Equals(stateElement.GetString(), "PAUSED", StringComparison.Ordinal)
            || !root.TryGetProperty("tasks", out var tasks)
            || tasks.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var hasTasks = false;
        foreach (var task in tasks.EnumerateArray())
        {
            hasTasks = true;
            if (!task.TryGetProperty("state", out var taskStateElement)
                || !string.Equals(
                    taskStateElement.GetString(),
                    "PAUSED",
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return hasTasks;
    }

    /// <summary>
    /// 调用 GET /connectors/{name}/offsets 读取 CDC 位点，解析 MySQL binlog(file/pos)
    /// 或 SQL Server commit_lsn 并返回 CdcDeliveryPosition；失败返回 null。
    /// </summary>
    public async Task<CdcDeliveryPosition?> TryReadConnectorPositionAsync(
        string connectorName,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync($"/connectors/{connectorName}/offsets", cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync(
                KafkaMessagingJsonSerializerContext.Default.ConnectorOffsetsResponse,
                cancellationToken)
            .ConfigureAwait(false);
        if (payload?.Offsets is not { Count: > 0 })
        {
            return null;
        }

        var offset = payload.Offsets[0].Offset;
        if (offset is null)
        {
            return null;
        }

        if (offset.TryGetValue("file", out var file)
            && offset.TryGetValue("pos", out var positionElement)
            && positionElement.TryGetInt64(out var position))
        {
            var fileName = file.GetString();
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return CdcDeliveryPosition.ForMySql(null, fileName, position);
            }
        }

        if (offset.TryGetValue("commit_lsn", out var commitLsnElement))
        {
            var commitLsn = commitLsnElement.GetString();
            if (!string.IsNullOrWhiteSpace(commitLsn))
            {
                return CdcDeliveryPosition.ForSqlServer(null, commitLsn);
            }
        }

        return null;
    }

    /// <summary>
    /// 调用 GET /connectors/{name}/status 获取连接器状态 JSON 原文；
    /// 404 返回 null，其余失败抛出。
    /// </summary>
    public async Task<string?> TryGetConnectorStatusAsync(
        string connectorName,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync($"/connectors/{connectorName}/status", cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>释放本实例创建的 HttpClient；外部注入的 HttpClient 不由本实例释放。</summary>
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<ConnectorHealth> TryGetConnectorHealthAsync(
        string connectorName,
        CancellationToken cancellationToken)
    {
        var statusJson = await TryGetConnectorStatusAsync(connectorName, cancellationToken)
            .ConfigureAwait(false);
        return ParseConnectorHealth(statusJson);
    }

    private static ConnectorHealth ParseConnectorHealth(string? statusJson)
    {
        if (string.IsNullOrWhiteSpace(statusJson))
        {
            return ConnectorHealth.Unknown;
        }

        using var document = JsonDocument.Parse(statusJson);
        var root = document.RootElement;
        if (!root.TryGetProperty("connector", out var connector)
            || !connector.TryGetProperty("state", out var connectorStateElement))
        {
            return ConnectorHealth.Unknown;
        }

        var connectorState = connectorStateElement.GetString();
        if (!string.Equals(connectorState, "RUNNING", StringComparison.Ordinal))
        {
            return ConnectorHealth.Pending;
        }

        if (!root.TryGetProperty("tasks", out var tasks) || tasks.ValueKind != JsonValueKind.Array)
        {
            return ConnectorHealth.Pending;
        }

        var hasTasks = false;
        foreach (var task in tasks.EnumerateArray())
        {
            hasTasks = true;
            if (!task.TryGetProperty("state", out var taskStateElement))
            {
                return ConnectorHealth.Pending;
            }

            var taskState = taskStateElement.GetString();
            if (string.Equals(taskState, "FAILED", StringComparison.Ordinal))
            {
                return ConnectorHealth.Failed;
            }

            if (!string.Equals(taskState, "RUNNING", StringComparison.Ordinal))
            {
                return ConnectorHealth.Pending;
            }
        }

        return hasTasks ? ConnectorHealth.Running : ConnectorHealth.Pending;
    }

    internal sealed record ConnectorRegistration(
        string Name,
        IReadOnlyDictionary<string, string> Config);

    internal sealed class ConnectorOffsetsResponse
    {
        [JsonPropertyName("offsets")]
        public List<ConnectorOffsetEntry> Offsets { get; init; } = [];
    }

    internal sealed class ConnectorOffsetEntry
    {
        [JsonPropertyName("offset")]
        public Dictionary<string, JsonElement>? Offset { get; init; }
    }

    private enum ConnectorHealth
    {
        Unknown,
        Pending,
        Running,
        Failed,
    }
}
