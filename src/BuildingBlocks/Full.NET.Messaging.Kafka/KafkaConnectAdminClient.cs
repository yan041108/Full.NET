using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Messaging.Abstractions;

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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public KafkaConnectAdminClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _ownsHttpClient = false;
    }

    public KafkaConnectAdminClient(Uri connectBaseUri, TimeSpan? timeout = null)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = connectBaseUri,
            Timeout = timeout ?? TimeSpan.FromSeconds(60),
        };
        _ownsHttpClient = true;
    }

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

    public async Task RegisterConnectorAsync(
        string connectorName,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorName);
        ArgumentNullException.ThrowIfNull(config);
        var payload = new ConnectorRegistration(connectorName, config);
        using var response = await _httpClient
            .PostAsJsonAsync("/connectors", payload, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // Kafka Connect 可能在错误体中回显提交的连接器配置，其中包含数据库口令。
            throw new InvalidOperationException(
                $"Connector registration failed with HTTP status {(int)response.StatusCode}.");
        }
    }

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

    public async Task PauseConnectorAsync(string connectorName, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .PutAsync($"/connectors/{connectorName}/pause", null, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task ResumeConnectorAsync(string connectorName, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .PutAsync($"/connectors/{connectorName}/resume", null, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

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
        if (!document.RootElement.TryGetProperty("connector", out var connector)
            || !connector.TryGetProperty("state", out var stateElement))
        {
            return false;
        }

        return string.Equals(stateElement.GetString(), "PAUSED", StringComparison.Ordinal);
    }

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

        var payload = await response.Content.ReadFromJsonAsync<ConnectorOffsetsResponse>(
                JsonOptions,
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

    private sealed record ConnectorRegistration(
        string Name,
        IReadOnlyDictionary<string, string> Config);

    private sealed class ConnectorOffsetsResponse
    {
        [JsonPropertyName("offsets")]
        public List<ConnectorOffsetEntry> Offsets { get; init; } = [];
    }

    private sealed class ConnectorOffsetEntry
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
