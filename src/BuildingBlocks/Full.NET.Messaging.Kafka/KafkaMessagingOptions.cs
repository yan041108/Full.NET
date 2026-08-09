using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// Kafka Provider configuration. Secret fields must not appear in logs or ToString.
/// </summary>
public sealed class KafkaMessagingOptions
{
    public const string SectionName = "Messaging:Kafka";

    public const int MinMessageMaxBytes = 1_024;

    public const int MaxMessageMaxBytes = 10_485_760;

    public bool Enabled { get; set; }

    public string? BootstrapServers { get; set; }

    public string SecurityProtocol { get; set; } = "Plaintext";

    public string? SaslMechanism { get; set; }

    public string? SaslUsername { get; set; }

    public string? SaslPassword { get; set; }

    public string? ClientId { get; set; }

    public string? ConsumerInstanceId { get; set; }

    public int SessionTimeoutMilliseconds { get; set; } = 45_000;

    public int MaxPollIntervalMilliseconds { get; set; } = 300_000;

    public int HandlerHeartbeatMilliseconds { get; set; } = 250;

    public int ConsumerQueueMaxMessagesKbytes { get; set; } = 2_048;

    public int UncommittedRetryBackoffMilliseconds { get; set; } = 1_000;

    public int OwnershipRevokedBackoffMilliseconds { get; set; } = 30_000;

    public int DeliveryTimeoutMilliseconds { get; set; } = 120_000;

    public int MessageMaxBytes { get; set; } = 1_048_576;

    public string[] RetryStages { get; set; } = ["5s", "1m", "15m"];

    public bool EnableAutoCommit { get; set; }

    public string Acks { get; set; } = "All";

    public bool EnableIdempotence { get; set; } = true;

    public int ShutdownDrainSeconds { get; set; } = 30;

    public ConsumerConfig BuildConsumerConfig(string consumerGroupId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerGroupId);

        var config = new ConsumerConfig
        {
            BootstrapServers = BootstrapServers,
            GroupId = consumerGroupId,
            ClientId = ResolveClientId(consumerGroupId),
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            SessionTimeoutMs = SessionTimeoutMilliseconds,
            MaxPollIntervalMs = MaxPollIntervalMilliseconds,
            FetchMaxBytes = MessageMaxBytes,
            QueuedMinMessages = 1,
            QueuedMaxMessagesKbytes = ConsumerQueueMaxMessagesKbytes,
            AutoOffsetReset = AutoOffsetReset.Earliest,
        };

        ApplySecurity(config);
        return config;
    }

    public ProducerConfig BuildProducerConfig()
    {
        var config = new ProducerConfig
        {
            BootstrapServers = BootstrapServers,
            ClientId = ClientId,
            Acks = Confluent.Kafka.Acks.All,
            EnableIdempotence = true,
            MessageMaxBytes = MessageMaxBytes,
            MessageTimeoutMs = DeliveryTimeoutMilliseconds,
        };

        ApplySecurity(config);
        return config;
    }

    internal ClientConfig BuildClientConfig()
    {
        var config = new ClientConfig
        {
            BootstrapServers = BootstrapServers,
            ClientId = ClientId,
        };

        ApplySecurity(config);
        return config;
    }

    public override string ToString() =>
        $"{SectionName} Enabled={Enabled}; BootstrapServers={BootstrapServers}; "
        + $"SecurityProtocol={SecurityProtocol}; SaslMechanism={SaslMechanism}; "
        + $"SaslUsername={SaslUsername}; SaslPassword=***; ClientId={ClientId}; "
        + $"ConsumerInstanceId={ConsumerInstanceId}; MessageMaxBytes={MessageMaxBytes}; "
        + $"EnableAutoCommit={EnableAutoCommit}; Acks={Acks}; EnableIdempotence={EnableIdempotence}";

    private string ResolveClientId(string consumerGroupId) =>
        string.IsNullOrWhiteSpace(ConsumerInstanceId)
            ? $"{ClientId}-{consumerGroupId}"
            : ConsumerInstanceId!;

    private void ApplySecurity(ClientConfig config)
    {
        if (!Enum.TryParse<Confluent.Kafka.SecurityProtocol>(
                SecurityProtocol,
                ignoreCase: true,
                out var parsedSecurityProtocol))
        {
            throw new InvalidOperationException(
                $"{SectionName}:SecurityProtocol '{SecurityProtocol}' is not supported.");
        }

        config.SecurityProtocol = parsedSecurityProtocol;
        if (parsedSecurityProtocol is Confluent.Kafka.SecurityProtocol.SaslSsl or Confluent.Kafka.SecurityProtocol.SaslPlaintext)
        {
            if (!Enum.TryParse<SaslMechanism>(
                    SaslMechanism,
                    ignoreCase: true,
                    out var saslMechanism))
            {
                throw new InvalidOperationException(
                    $"{SectionName}:SaslMechanism is required when SecurityProtocol uses SASL.");
            }

            config.SaslMechanism = saslMechanism;
            config.SaslUsername = SaslUsername;
            config.SaslPassword = SaslPassword;
        }
    }
}

internal static class KafkaMessagingOptionsValidation
{
    public static ValidateOptionsResult Validate(
        KafkaMessagingOptions options,
        string? environmentName)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (options.EnableAutoCommit)
        {
            failures.Add($"{KafkaMessagingOptions.SectionName}:EnableAutoCommit must remain false.");
        }

        if (!string.Equals(options.Acks, "All", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{KafkaMessagingOptions.SectionName}:Acks must be All.");
        }

        if (!options.EnableIdempotence)
        {
            failures.Add($"{KafkaMessagingOptions.SectionName}:EnableIdempotence must be true.");
        }

        if (options.MessageMaxBytes is < KafkaMessagingOptions.MinMessageMaxBytes
            or > KafkaMessagingOptions.MaxMessageMaxBytes)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:MessageMaxBytes must be between "
                + $"{KafkaMessagingOptions.MinMessageMaxBytes} and "
                + $"{KafkaMessagingOptions.MaxMessageMaxBytes}.");
        }

        if (options.SessionTimeoutMilliseconds < 1_000
            || options.MaxPollIntervalMilliseconds < options.SessionTimeoutMilliseconds)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:SessionTimeoutMilliseconds and "
                + "MaxPollIntervalMilliseconds are out of range.");
        }

        if (options.HandlerHeartbeatMilliseconds is < 10
            || options.HandlerHeartbeatMilliseconds >= options.SessionTimeoutMilliseconds
            || options.HandlerHeartbeatMilliseconds >= options.MaxPollIntervalMilliseconds)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:HandlerHeartbeatMilliseconds must be at least 10 and less than SessionTimeoutMilliseconds and MaxPollIntervalMilliseconds.");
        }

        if (options.ConsumerQueueMaxMessagesKbytes is < 1 or > 102_400)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:ConsumerQueueMaxMessagesKbytes must be between 1 and 102400.");
        }

        if (options.UncommittedRetryBackoffMilliseconds is < 100 or > 60_000)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:UncommittedRetryBackoffMilliseconds must be between 100 and 60000.");
        }

        if (options.OwnershipRevokedBackoffMilliseconds is < 1_000 or > 300_000)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:OwnershipRevokedBackoffMilliseconds must be between 1000 and 300000.");
        }

        if (options.DeliveryTimeoutMilliseconds < 1_000)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:DeliveryTimeoutMilliseconds must be positive.");
        }

        var retryDelays = new List<TimeSpan>(options.RetryStages.Length);
        var retryStagesValid = options.RetryStages.Length > 0;
        foreach (var stage in options.RetryStages)
        {
            if (!KafkaRetryStageParser.TryParse(stage, out var delay))
            {
                retryStagesValid = false;
                break;
            }

            retryDelays.Add(delay);
        }

        if (!retryStagesValid)
        {
            failures.Add($"{KafkaMessagingOptions.SectionName}:RetryStages must be configured.");
        }
        else if (retryDelays.Zip(retryDelays.Skip(1), (left, right) => right > left)
                 .Any(increases => !increases))
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:RetryStages must be strictly increasing.");
        }

        if (IsProductionLike(environmentName))
        {
            ValidateProduction(options, failures);
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateProduction(
        KafkaMessagingOptions options,
        ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.BootstrapServers))
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:BootstrapServers is required in Production.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add($"{KafkaMessagingOptions.SectionName}:ClientId is required in Production.");
        }

        if (string.IsNullOrWhiteSpace(options.ConsumerInstanceId))
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:ConsumerInstanceId is required in Production.");
        }

        var securityProtocolConfigured = Enum.TryParse<Confluent.Kafka.SecurityProtocol>(
            options.SecurityProtocol,
            ignoreCase: true,
            out var parsedSecurityProtocol);
        if (!securityProtocolConfigured || parsedSecurityProtocol is Confluent.Kafka.SecurityProtocol.Plaintext)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:SecurityProtocol must use TLS in Production.");
        }

        if (securityProtocolConfigured
            && parsedSecurityProtocol is Confluent.Kafka.SecurityProtocol.SaslSsl or Confluent.Kafka.SecurityProtocol.SaslPlaintext)
        {
            if (string.IsNullOrWhiteSpace(options.SaslMechanism))
            {
                failures.Add(
                    $"{KafkaMessagingOptions.SectionName}:SaslMechanism is required in Production.");
            }

            if (string.IsNullOrWhiteSpace(options.SaslUsername))
            {
                failures.Add(
                    $"{KafkaMessagingOptions.SectionName}:SaslUsername is required in Production.");
            }

            if (string.IsNullOrWhiteSpace(options.SaslPassword))
            {
                failures.Add(
                    $"{KafkaMessagingOptions.SectionName}:SaslPassword is required in Production.");
            }
        }
    }

    internal static bool IsProductionLike(string? environmentName) =>
        string.Equals(
            environmentName,
            Environments.Production,
            StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            environmentName,
            Environments.Staging,
            StringComparison.OrdinalIgnoreCase);
}

internal sealed class KafkaMessagingOptionsValidator(IHostEnvironment hostEnvironment)
    : IValidateOptions<KafkaMessagingOptions>
{
    public ValidateOptionsResult Validate(string? name, KafkaMessagingOptions options) =>
        KafkaMessagingOptionsValidation.Validate(options, hostEnvironment.EnvironmentName);
}
