using System.Net.Http.Json;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>
/// Debezium Kafka Connect REST 管理客户端；仅用于集成测试控制面。
/// </summary>
public sealed class DebeziumConnectAdminClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public DebeziumConnectAdminClient(Uri connectBaseUri)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = connectBaseUri,
            Timeout = TimeSpan.FromSeconds(60),
        };
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

    public async Task<bool> WaitUntilReadyAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await _httpClient.GetAsync("/", cancellationToken).ConfigureAwait(false);
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
        var payload = new ConnectorRegistration(connectorName, config);
        using var response = await _httpClient
            .PostAsJsonAsync("/connectors", payload, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Connector registration failed ({(int)response.StatusCode}): {body}");
        }
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

    public async Task DeleteConnectorAsync(string connectorName, CancellationToken cancellationToken = default)
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

    public void Dispose() => _httpClient.Dispose();

    private async Task<ConnectorHealth> TryGetConnectorHealthAsync(
        string connectorName,
        CancellationToken cancellationToken = default)
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

    private sealed record ConnectorRegistration(string Name, IReadOnlyDictionary<string, string> Config);

    internal enum ConnectorHealth
    {
        Unknown,
        Pending,
        Running,
        Failed,
    }
}
