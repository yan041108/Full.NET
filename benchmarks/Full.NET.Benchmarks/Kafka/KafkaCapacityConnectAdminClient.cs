using System.Net.Http.Json;
using System.Text.Json;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 通过 Kafka Connect REST 注册、健康检查与删除容量 Connector。
/// </summary>
public sealed class KafkaCapacityConnectAdminClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;
    private readonly int healthTimeoutSeconds;

    public KafkaCapacityConnectAdminClient(
        KafkaCapacityConnectConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!Uri.TryCreate(configuration.BaseUri, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidDataException("Connect BaseUri is invalid.");
        }

        healthTimeoutSeconds = configuration.HealthTimeoutSeconds;
        httpClient = new HttpClient
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(configuration.RequestTimeoutSeconds),
        };
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
                using var response = await httpClient
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
        using var response = await httpClient
            .PostAsJsonAsync("/connectors", payload, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new KafkaCapacityControlPlaneException(
                "connect_register_failed",
                $"Connector registration failed ({(int)response.StatusCode}).");
        }
    }

    public async Task<bool> WaitForConnectorHealthyAsync(
        string connectorName,
        CancellationToken cancellationToken = default) =>
        await WaitForConnectorHealthyAsync(
            connectorName,
            TimeSpan.FromSeconds(healthTimeoutSeconds),
            cancellationToken).ConfigureAwait(false);

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
        using var response = await httpClient
            .DeleteAsync($"/connectors/{connectorName}", cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new KafkaCapacityControlPlaneException(
                "connect_delete_failed",
                $"Connector deletion failed ({(int)response.StatusCode}).");
        }
    }

    public async Task<string?> TryGetConnectorStatusAsync(
        string connectorName,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient
            .GetAsync($"/connectors/{connectorName}/status", cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void Dispose() => httpClient.Dispose();

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

    private enum ConnectorHealth
    {
        Unknown,
        Pending,
        Running,
        Failed,
    }
}
