using System.Net.Http;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 在 Kafka 或 Connector 副作用前验证外部 Connect REST 控制面可达。
/// </summary>
public sealed class KafkaCapacityConnectPreflight(
    KafkaCapacityConnectConfiguration configuration) : IKafkaCapacityDriverPreflight
{
    public async Task ValidateAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration.BaseUri)
            || configuration.RequestTimeoutSeconds <= 0
            || configuration.HealthTimeoutSeconds <= 0
            || string.IsNullOrWhiteSpace(configuration.ConnectorNamePrefix))
        {
            throw Rejected("connect_configuration_invalid");
        }

        if (!Uri.TryCreate(configuration.BaseUri, UriKind.Absolute, out var baseUri))
        {
            throw Rejected("connect_base_uri_invalid");
        }

        try
        {
            using var client = new HttpClient
            {
                BaseAddress = baseUri,
                Timeout = TimeSpan.FromSeconds(configuration.RequestTimeoutSeconds),
            };
            using var response = await client
                .GetAsync("/", cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw Rejected("connect_not_ready");
            }
        }
        catch (KafkaCapacityControlPlaneException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new KafkaCapacityControlPlaneException(
                "connect_preflight_failed",
                $"Kafka capacity Connect preflight failed without exposing endpoint details ({exception.GetType().Name}).");
        }
    }

    private static KafkaCapacityControlPlaneException Rejected(string reasonCode) =>
        new(reasonCode, "Kafka capacity Connect preflight rejected the target environment.");
}
