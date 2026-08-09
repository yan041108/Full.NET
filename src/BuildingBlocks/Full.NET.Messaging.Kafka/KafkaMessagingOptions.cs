using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// Kafka Consumer Group 使用的客户端协议。
/// </summary>
public enum KafkaConsumerGroupProtocolMode
{
    /// <summary>使用传统客户端协调协议。</summary>
    Classic = 0,

    /// <summary>使用 Kafka 4.x KIP-848 Consumer 协议。</summary>
    Consumer = 1,
}

/// <summary>
/// Classic 协议下的分区分配策略迁移状态。
/// </summary>
public enum KafkaClassicPartitionAssignmentMode
{
    /// <summary>保持与存量 eager Consumer 兼容的 Range Assignor。</summary>
    LegacyRange = 0,

    /// <summary>使用增量 Cooperative Sticky Assignor，启用前必须完成离线迁移。</summary>
    CooperativeSticky = 1,
}

/// <summary>
/// Kafka Provider 配置。Secret 字段禁止进入日志或 <see cref="ToString"/>。
/// </summary>
public sealed class KafkaMessagingOptions
{
    public const string SectionName = "Messaging:Kafka";

    public const int MinMessageMaxBytes = 1_024;

    public const int MaxMessageMaxBytes = 10_485_760;

    public const int MaxProducerBatchSizeBytes = 1_048_576;

    public bool Enabled { get; set; }

    public string? BootstrapServers { get; set; }

    public string SecurityProtocol { get; set; } = "Plaintext";

    public string? SaslMechanism { get; set; }

    public string? SaslUsername { get; set; }

    public string? SaslPassword { get; set; }

    public string? ClientId { get; set; }

    /// <summary>
    /// Consumer Group 内唯一的静态成员标识；只有跨进程重启保持稳定时才能减少 Rebalance。
    /// </summary>
    public string? ConsumerInstanceId { get; set; }

    /// <summary>
    /// Consumer Group 协议；Consumer 模式要求 Kafka Broker 4.x 兼容门禁通过。
    /// </summary>
    public KafkaConsumerGroupProtocolMode ConsumerGroupProtocol { get; set; } =
        KafkaConsumerGroupProtocolMode.Classic;

    /// <summary>
    /// 运维确认的 Broker 主版本，仅用于协议互斥校验，不替代真实兼容测试。
    /// </summary>
    public int BrokerMajorVersion { get; set; } = 3;

    /// <summary>
    /// Classic 协议的分区分配策略；迁移完成前保持与旧客户端兼容的 Range。
    /// </summary>
    public KafkaClassicPartitionAssignmentMode ClassicPartitionAssignment { get; set; } =
        KafkaClassicPartitionAssignmentMode.LegacyRange;

    /// <summary>
    /// 表示目标 Consumer Group 已排空并完成 Cooperative Sticky 离线迁移演练。
    /// </summary>
    public bool CooperativeStickyMigrationCompleted { get; set; }

    public int SessionTimeoutMilliseconds { get; set; } = 45_000;

    public int MaxPollIntervalMilliseconds { get; set; } = 300_000;

    public int HandlerHeartbeatMilliseconds { get; set; } = 250;

    public int CompletionPollMilliseconds { get; set; } = 5;

    public int ConsumerQueueMaxMessagesKbytes { get; set; } = 2_048;

    /// <summary>
    /// 单个 Consumer Group 在应用处理通道中的全局消息高水位。
    /// </summary>
    public int ConsumerBufferHighWatermark { get; set; } = 256;

    /// <summary>
    /// 全局 Buffer 降至该深度后才恢复因全局背压暂停的分区。
    /// </summary>
    public int ConsumerBufferLowWatermark { get; set; } = 128;

    /// <summary>
    /// 单个 Topic Partition 的应用处理通道高水位；默认 1 保持原有单在途语义。
    /// </summary>
    public int PartitionBufferHighWatermark { get; set; } = 1;

    /// <summary>
    /// 单分区 Buffer 降至该深度后才恢复该分区。
    /// </summary>
    public int PartitionBufferLowWatermark { get; set; }

    /// <summary>
    /// 单分区按业务 Key 映射的固定并行槽数；同一槽内保持串行。
    /// </summary>
    public int PartitionKeyConcurrencySlots { get; set; } = 1;

    /// <summary>
    /// 连续安全 Offset 的 Broker 提交模式。
    /// </summary>
    public KafkaOffsetCommitMode OffsetCommitMode { get; set; } = KafkaOffsetCommitMode.PerMessage;

    /// <summary>
    /// 周期提交间隔，单位为毫秒；提交失败后也使用该间隔退避。
    /// </summary>
    public int OffsetCommitIntervalMilliseconds { get; set; } = 1_000;

    /// <summary>
    /// 周期模式在积累该数量的安全水位更新后提前触发批量提交。
    /// </summary>
    public int OffsetCommitBatchSize { get; set; } = 100;

    /// <summary>
    /// 表示周期提交已经完成 Rebalance、宕机重投与回退故障矩阵验证。
    /// </summary>
    public bool PeriodicOffsetCommitVerified { get; set; }

    public int UncommittedRetryBackoffMilliseconds { get; set; } = 1_000;

    public int OwnershipRevokedBackoffMilliseconds { get; set; } = 30_000;

    public int DeliveryTimeoutMilliseconds { get; set; } = 120_000;

    public int MessageMaxBytes { get; set; } = 1_048_576;

    /// <summary>
    /// Producer 为合并批次最多等待的毫秒数。
    /// </summary>
    public int ProducerLingerMilliseconds { get; set; } = 5;

    /// <summary>
    /// 单个 Producer 批次的目标字节上限，允许范围为 1 KiB 至 1 MiB。
    /// </summary>
    public int ProducerBatchSizeBytes { get; set; } = 65_536;

    /// <summary>
    /// 当前 Producer 实例跨 Topic 共享的本地队列消息数上限。
    /// </summary>
    public int ProducerQueueMaxMessages { get; set; } = 20_000;

    /// <summary>
    /// 当前 Producer 实例跨 Topic 共享的本地队列内存上限，单位为 KiB。
    /// </summary>
    public int ProducerQueueMaxKbytes { get; set; } = 65_536;

    /// <summary>
    /// 每个 Broker 连接的最大在途请求数；幂等 Producer 不得超过 5。
    /// </summary>
    public int ProducerMaxInFlightRequests { get; set; } = 5;

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
            GroupProtocol = ConsumerGroupProtocol == KafkaConsumerGroupProtocolMode.Consumer
                ? GroupProtocol.Consumer
                : GroupProtocol.Classic,
            GroupInstanceId = string.IsNullOrWhiteSpace(ConsumerInstanceId)
                ? null
                : ConsumerInstanceId,
            MaxPollIntervalMs = MaxPollIntervalMilliseconds,
            FetchMaxBytes = MessageMaxBytes,
            QueuedMinMessages = 1,
            QueuedMaxMessagesKbytes = ConsumerQueueMaxMessagesKbytes,
            AutoOffsetReset = AutoOffsetReset.Earliest,
        };

        if (ConsumerGroupProtocol == KafkaConsumerGroupProtocolMode.Classic)
        {
            config.PartitionAssignmentStrategy =
                ClassicPartitionAssignment == KafkaClassicPartitionAssignmentMode.CooperativeSticky
                    ? PartitionAssignmentStrategy.CooperativeSticky
                    : PartitionAssignmentStrategy.Range;
            config.SessionTimeoutMs = SessionTimeoutMilliseconds;
        }

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
            LingerMs = ProducerLingerMilliseconds,
            BatchSize = ProducerBatchSizeBytes,
            QueueBufferingMaxMessages = ProducerQueueMaxMessages,
            QueueBufferingMaxKbytes = ProducerQueueMaxKbytes,
            MaxInFlight = ProducerMaxInFlightRequests,
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
        + $"ConsumerGroupProtocol={ConsumerGroupProtocol}; BrokerMajorVersion={BrokerMajorVersion}; "
        + $"ClassicPartitionAssignment={ClassicPartitionAssignment}; "
        + $"CooperativeStickyMigrationCompleted={CooperativeStickyMigrationCompleted}; "
        + $"HandlerHeartbeatMilliseconds={HandlerHeartbeatMilliseconds}; "
        + $"CompletionPollMilliseconds={CompletionPollMilliseconds}; "
        + $"ConsumerBufferHighWatermark={ConsumerBufferHighWatermark}; "
        + $"ConsumerBufferLowWatermark={ConsumerBufferLowWatermark}; "
        + $"PartitionBufferHighWatermark={PartitionBufferHighWatermark}; "
        + $"PartitionBufferLowWatermark={PartitionBufferLowWatermark}; "
        + $"PartitionKeyConcurrencySlots={PartitionKeyConcurrencySlots}; "
        + $"OffsetCommitMode={OffsetCommitMode}; "
        + $"OffsetCommitIntervalMilliseconds={OffsetCommitIntervalMilliseconds}; "
        + $"OffsetCommitBatchSize={OffsetCommitBatchSize}; "
        + $"PeriodicOffsetCommitVerified={PeriodicOffsetCommitVerified}; "
        + $"ProducerLingerMilliseconds={ProducerLingerMilliseconds}; "
        + $"ProducerBatchSizeBytes={ProducerBatchSizeBytes}; "
        + $"ProducerQueueMaxMessages={ProducerQueueMaxMessages}; "
        + $"ProducerQueueMaxKbytes={ProducerQueueMaxKbytes}; "
        + $"ProducerMaxInFlightRequests={ProducerMaxInFlightRequests}; "
        + $"EnableAutoCommit={EnableAutoCommit}; Acks={Acks}; EnableIdempotence={EnableIdempotence}";

    private string ResolveClientId(string consumerGroupId) =>
        string.IsNullOrWhiteSpace(ClientId)
            ? consumerGroupId
            : $"{ClientId}-{consumerGroupId}";

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

        if (options.ConsumerGroupProtocol == KafkaConsumerGroupProtocolMode.Consumer
            && options.BrokerMajorVersion < 4)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:BrokerMajorVersion must be at least 4 "
                + "when ConsumerGroupProtocol is Consumer.");
        }

        if (!Enum.IsDefined(options.ConsumerGroupProtocol))
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:ConsumerGroupProtocol is not supported.");
        }

        if (!Enum.IsDefined(options.ClassicPartitionAssignment))
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:ClassicPartitionAssignment is not supported.");
        }

        if (options.ConsumerGroupProtocol == KafkaConsumerGroupProtocolMode.Classic
            && options.ClassicPartitionAssignment == KafkaClassicPartitionAssignmentMode.CooperativeSticky
            && !options.CooperativeStickyMigrationCompleted)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:CooperativeStickyMigrationCompleted must be true "
                + "before changing an existing Classic Consumer Group to CooperativeSticky.");
        }

        if (options.HandlerHeartbeatMilliseconds is < 10
            || options.HandlerHeartbeatMilliseconds >= options.SessionTimeoutMilliseconds
            || options.HandlerHeartbeatMilliseconds >= options.MaxPollIntervalMilliseconds)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:HandlerHeartbeatMilliseconds must be at least 10 and less than SessionTimeoutMilliseconds and MaxPollIntervalMilliseconds.");
        }

        if (options.CompletionPollMilliseconds < 1
            || options.CompletionPollMilliseconds > options.HandlerHeartbeatMilliseconds)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:CompletionPollMilliseconds must be between 1 and HandlerHeartbeatMilliseconds.");
        }

        if (options.ConsumerQueueMaxMessagesKbytes is < 1 or > 102_400)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:ConsumerQueueMaxMessagesKbytes must be between 1 and 102400.");
        }

        if (options.ConsumerBufferHighWatermark is < 1 or > 1_000_000)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:ConsumerBufferHighWatermark must be between 1 and 1000000.");
        }
        else if (options.ConsumerBufferLowWatermark < 0
                 || options.ConsumerBufferLowWatermark >= options.ConsumerBufferHighWatermark)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:ConsumerBufferLowWatermark must be non-negative "
                + "and less than ConsumerBufferHighWatermark.");
        }

        if (options.PartitionBufferHighWatermark is < 1 or > 10_000)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:PartitionBufferHighWatermark must be between 1 and 10000.");
        }
        else if (options.PartitionBufferLowWatermark < 0
                 || options.PartitionBufferLowWatermark >= options.PartitionBufferHighWatermark)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:PartitionBufferLowWatermark must be non-negative "
                + "and less than PartitionBufferHighWatermark.");
        }

        if (options.PartitionKeyConcurrencySlots is < 1 or > 64
            || options.PartitionKeyConcurrencySlots > options.PartitionBufferHighWatermark)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:PartitionKeyConcurrencySlots must be between 1 and 64 "
                + "and cannot exceed PartitionBufferHighWatermark.");
        }

        if (!Enum.IsDefined(options.OffsetCommitMode))
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:OffsetCommitMode is not supported.");
        }

        if (options.OffsetCommitIntervalMilliseconds is < 100 or > 60_000)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:OffsetCommitIntervalMilliseconds must be between 100 and 60000.");
        }

        if (options.OffsetCommitBatchSize is < 1 or > 10_000)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:OffsetCommitBatchSize must be between 1 and 10000.");
        }

        if (IsProductionLike(environmentName)
            && options.OffsetCommitMode == KafkaOffsetCommitMode.PeriodicWatermark
            && !options.PeriodicOffsetCommitVerified)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:PeriodicOffsetCommitVerified must be true "
                + "before PeriodicWatermark can be enabled in Production or Staging.");
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

        if (options.ProducerLingerMilliseconds is < 0 or > 1_000)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:ProducerLingerMilliseconds must be between 0 and 1000.");
        }

        if (options.ProducerBatchSizeBytes is < KafkaMessagingOptions.MinMessageMaxBytes
            or > KafkaMessagingOptions.MaxProducerBatchSizeBytes)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:ProducerBatchSizeBytes must be between "
                + $"{KafkaMessagingOptions.MinMessageMaxBytes} and "
                + $"{KafkaMessagingOptions.MaxProducerBatchSizeBytes}.");
        }

        if (options.ProducerQueueMaxMessages is < 1 or > 1_000_000)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:ProducerQueueMaxMessages must be between 1 and 1000000.");
        }

        if (options.ProducerQueueMaxKbytes is < 1_024 or > 2_097_152)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:ProducerQueueMaxKbytes must be between 1024 and 2097152.");
        }

        if (options.ProducerMaxInFlightRequests is < 1 or > 5)
        {
            failures.Add(
                $"{KafkaMessagingOptions.SectionName}:ProducerMaxInFlightRequests must be between 1 and 5 "
                + "when idempotence is enabled.");
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
