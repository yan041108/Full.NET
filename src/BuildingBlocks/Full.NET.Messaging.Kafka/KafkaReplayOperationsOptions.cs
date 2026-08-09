using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// API 同步范围重放的独立安全门禁；常驻 Worker 的 Enabled 状态不能隐式开放运维重放。
/// </summary>
internal sealed class KafkaReplayOperationsOptions
{
    public const string SectionName = "Messaging:KafkaReplay";

    public bool Enabled { get; set; }

    public int MaximumSynchronousMessages { get; set; } = 1_000;

    public int ExecutionTimeoutSeconds { get; set; } = 45;
}

internal sealed class KafkaReplayOperationsOptionsValidator(
    IOptions<KafkaMessagingOptions> kafkaOptions,
    IHostEnvironment hostEnvironment)
    : IValidateOptions<KafkaReplayOperationsOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        KafkaReplayOperationsOptions options)
    {
        var failures = new List<string>();
        if (options.MaximumSynchronousMessages is < 1 or > 1_000)
        {
            failures.Add(
                $"{KafkaReplayOperationsOptions.SectionName}:MaximumSynchronousMessages must be between 1 and 1000.");
        }

        if (options.ExecutionTimeoutSeconds is < 5 or > 45)
        {
            failures.Add(
                $"{KafkaReplayOperationsOptions.SectionName}:ExecutionTimeoutSeconds must be between 5 and 45.");
        }

        if (options.Enabled)
        {
            if (string.IsNullOrWhiteSpace(kafkaOptions.Value.BootstrapServers))
            {
                failures.Add(
                    $"{KafkaMessagingOptions.SectionName}:BootstrapServers is required when Kafka replay is enabled.");
            }

            if (string.IsNullOrWhiteSpace(kafkaOptions.Value.ClientId))
            {
                failures.Add(
                    $"{KafkaMessagingOptions.SectionName}:ClientId is required when Kafka replay is enabled.");
            }

            var protocolConfigured = Enum.TryParse<Confluent.Kafka.SecurityProtocol>(
                kafkaOptions.Value.SecurityProtocol,
                ignoreCase: true,
                out var securityProtocol);
            if (!protocolConfigured)
            {
                failures.Add(
                    $"{KafkaMessagingOptions.SectionName}:SecurityProtocol is not supported.");
            }
            else if (KafkaMessagingOptionsValidation.IsProductionLike(hostEnvironment.EnvironmentName)
                     && securityProtocol is Confluent.Kafka.SecurityProtocol.Plaintext
                         or Confluent.Kafka.SecurityProtocol.SaslPlaintext)
            {
                failures.Add(
                    $"{KafkaMessagingOptions.SectionName}:SecurityProtocol must use TLS when Kafka replay is enabled in Production or Staging.");
            }

            if (protocolConfigured
                && securityProtocol is SecurityProtocol.SaslSsl
                    or SecurityProtocol.SaslPlaintext)
            {
                if (string.IsNullOrWhiteSpace(kafkaOptions.Value.SaslMechanism)
                    || string.IsNullOrWhiteSpace(kafkaOptions.Value.SaslUsername)
                    || string.IsNullOrWhiteSpace(kafkaOptions.Value.SaslPassword))
                {
                    failures.Add(
                        $"{KafkaMessagingOptions.SectionName}:SaslMechanism, SaslUsername and SaslPassword are required for SASL replay connections.");
                }
                else if (!Enum.TryParse<SaslMechanism>(
                             kafkaOptions.Value.SaslMechanism,
                             ignoreCase: true,
                             out _))
                {
                    failures.Add(
                        $"{KafkaMessagingOptions.SectionName}:SaslMechanism is not supported.");
                }
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
