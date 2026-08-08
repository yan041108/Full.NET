using Confluent.Kafka;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Full.NET.Messaging.Kafka.Health;

internal sealed class KafkaHealthCheck(IOptions<KafkaMessagingOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        _ = context;
        if (!options.Value.Enabled)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Kafka messaging is disabled."));
        }

        try
        {
            using var admin = new AdminClientBuilder(options.Value.BuildClientConfig()).Build();
            _ = admin.GetMetadata(TimeSpan.FromSeconds(2));
            return Task.FromResult(HealthCheckResult.Healthy());
        }
        catch (KafkaException)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Kafka broker metadata is unavailable."));
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Kafka client configuration is invalid."));
        }
    }
}