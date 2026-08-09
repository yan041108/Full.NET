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
/// Debezium Kafka Connect REST 客户端；只用于回退控制面，不承载业务流量。
/// </summary>
internal sealed class KafkaConnectAdminClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public KafkaConnectAdminClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task PauseConnectorAsync(string connectorName, CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .PutAsync($"/connectors/{connectorName}/pause", null, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task ResumeConnectorAsync(string connectorName, CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .PutAsync($"/connectors/{connectorName}/resume", null, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> IsConnectorPausedAsync(
        string connectorName,
        CancellationToken cancellationToken)
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
        CancellationToken cancellationToken)
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
}
