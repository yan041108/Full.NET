using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace Full.NET.Logging.Kafka;

/// <summary>日志直发 Kafka 的独立配置；只在选择该路由时绑定和校验。</summary>
public sealed class KafkaLogProducerOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "FullNet:Logging:Kafka";

    /// <summary>Broker 地址列表，不得包含凭据。</summary>
    public string? BootstrapServers { get; set; }

    /// <summary>普通日志使用的 Topic。</summary>
    public string? GeneralTopic { get; set; }

    /// <summary>优先级日志使用的独立 Topic。</summary>
    public string? PriorityTopic { get; set; }

    /// <summary>只允许 Ssl 或 SaslSsl。</summary>
    public string SecurityProtocol { get; set; } = "Ssl";

    /// <summary>SaslSsl 使用的认证机制。</summary>
    public string? SaslMechanism { get; set; }

    /// <summary>SaslSsl 使用的用户名。</summary>
    public string? SaslUsername { get; set; }

    /// <summary>只从受信任的 Secret 源提供，禁止进入日志。</summary>
    public string? SaslPassword { get; set; }

    /// <summary>可选的 CA 证书路径。</summary>
    public string? SslCaLocation { get; set; }

    /// <summary>可选的客户端标识。</summary>
    public string? ClientId { get; set; }

    /// <summary>应用侧等待投递终态的消息数上限。</summary>
    public int MaxPendingMessages { get; set; } = 10_000;

    /// <summary>应用侧等待投递终态的估算字节上限。</summary>
    public long MaxPendingBytes { get; set; } = 67_108_864;

    /// <summary>SDK 侧队列的消息数上限。</summary>
    public int QueueBufferingMaxMessages { get; set; } = 10_000;

    /// <summary>SDK 侧队列的内存上限，单位 KiB。</summary>
    public int QueueBufferingMaxKbytes { get; set; } = 65_536;

    /// <summary>启用幂等 Producer 时每个 Broker 的最大在途请求数。</summary>
    public int MaxInFlightRequests { get; set; } = 5;

    /// <summary>单条消息上限，单位字节。</summary>
    public int MessageMaxBytes { get; set; } = 1_048_576;

    /// <summary>消息交付超时，单位毫秒。</summary>
    public int MessageTimeoutMs { get; set; } = 30_000;

    /// <summary>批次聚合等待时间，单位毫秒。</summary>
    public int LingerMs { get; set; } = 5;

    /// <summary>停机阶段允许 Flush 等待的最长时间，单位毫秒。</summary>
    public int ShutdownFlushTimeoutMs { get; set; } = 5_000;

    /// <summary>构造有边界、TLS 且幂等的 SDK 配置，不建立网络连接。</summary>
    public ProducerConfig BuildProducerConfig()
    {
        var validation = new KafkaLogProducerOptionsValidator().Validate(null, this);
        if (validation.Failed)
        {
            throw new OptionsValidationException(SectionName, typeof(KafkaLogProducerOptions), validation.Failures);
        }

        var config = new ProducerConfig
        {
            BootstrapServers = BootstrapServers,
            ClientId = ClientId,
            Acks = Confluent.Kafka.Acks.All,
            EnableIdempotence = true,
            EnableDeliveryReports = true,
            DeliveryReportFields = "none",
            SecurityProtocol = Enum.Parse<Confluent.Kafka.SecurityProtocol>(SecurityProtocol, true),
            MessageMaxBytes = MessageMaxBytes,
            MessageTimeoutMs = MessageTimeoutMs,
            LingerMs = LingerMs,
            QueueBufferingMaxMessages = QueueBufferingMaxMessages,
            QueueBufferingMaxKbytes = QueueBufferingMaxKbytes,
            MaxInFlight = MaxInFlightRequests,
            SslCaLocation = SslCaLocation,
        };

        if (config.SecurityProtocol == Confluent.Kafka.SecurityProtocol.SaslSsl)
        {
            config.SaslMechanism = Enum.Parse<Confluent.Kafka.SaslMechanism>(SaslMechanism!, true);
            config.SaslUsername = SaslUsername;
            config.SaslPassword = SaslPassword;
        }

        return config;
    }

    /// <summary>避免默认记录对象时暴露 Broker 或认证信息。</summary>
    public override string ToString() => $"{SectionName} [redacted]";
}

/// <summary>拒绝无 TLS、缺少双 Topic 或无法控制内存的直发配置。</summary>
public sealed class KafkaLogProducerOptionsValidator : IValidateOptions<KafkaLogProducerOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, KafkaLogProducerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();
        var prefix = KafkaLogProducerOptions.SectionName + ":";

        if (string.IsNullOrWhiteSpace(options.BootstrapServers)
            || options.BootstrapServers.Length > 2_048
            || options.BootstrapServers.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '@' or '/' or '?' or '#'))
        {
            errors.Add(prefix + "BootstrapServers must contain broker addresses without credentials.");
        }

        if (!ValidTopic(options.GeneralTopic))
        {
            errors.Add(prefix + "GeneralTopic must be a valid Kafka topic name.");
        }

        if (!ValidTopic(options.PriorityTopic)
            || string.Equals(options.GeneralTopic, options.PriorityTopic, StringComparison.Ordinal))
        {
            errors.Add(prefix + "PriorityTopic must be valid and different from GeneralTopic.");
        }

        var ssl = string.Equals(options.SecurityProtocol, "Ssl", StringComparison.OrdinalIgnoreCase);
        var saslSsl = string.Equals(options.SecurityProtocol, "SaslSsl", StringComparison.OrdinalIgnoreCase);
        if (!ssl && !saslSsl)
        {
            errors.Add(prefix + "SecurityProtocol must be Ssl or SaslSsl.");
        }

        if (saslSsl)
        {
            if (!Enum.TryParse<Confluent.Kafka.SaslMechanism>(options.SaslMechanism, true, out var mechanism)
                || mechanism is not (Confluent.Kafka.SaslMechanism.ScramSha256
                    or Confluent.Kafka.SaslMechanism.ScramSha512
                    or Confluent.Kafka.SaslMechanism.Plain))
            {
                errors.Add(prefix + "SaslMechanism must be Plain, ScramSha256 or ScramSha512.");
            }

            if (string.IsNullOrWhiteSpace(options.SaslUsername)
                || string.IsNullOrWhiteSpace(options.SaslPassword))
            {
                errors.Add(prefix + "SaslUsername and SaslPassword are required for SaslSsl.");
            }
        }
        else if (ssl && (!string.IsNullOrWhiteSpace(options.SaslMechanism)
                         || !string.IsNullOrWhiteSpace(options.SaslUsername)
                         || !string.IsNullOrWhiteSpace(options.SaslPassword)))
        {
            errors.Add(prefix + "SASL fields require SaslSsl.");
        }

        if (options.MaxPendingMessages is < 1 or > 1_000_000)
        {
            errors.Add(prefix + "MaxPendingMessages must be between 1 and 1000000.");
        }

        // 应用预算和 SDK 队列至少容纳一条允许的最大消息，避免合法事件永远无法入队。
        if (options.MaxPendingBytes < (long)options.MessageMaxBytes + KafkaLogProducerBudget.EnvelopeOverheadBytes
            || options.MaxPendingBytes > 1_073_741_824)
        {
            errors.Add(prefix + "MaxPendingBytes must hold one maximum-size message plus its envelope and be at most 1073741824.");
        }

        if (options.QueueBufferingMaxMessages is < 1 or > 1_000_000)
        {
            errors.Add(prefix + "QueueBufferingMaxMessages must be between 1 and 1000000.");
        }

        if (options.QueueBufferingMaxKbytes is < 1 or > 1_048_576)
        {
            errors.Add(prefix + "QueueBufferingMaxKbytes must be between 1 and 1048576.");
        }
        else if ((long)options.QueueBufferingMaxKbytes * 1_024 < options.MessageMaxBytes)
        {
            errors.Add(prefix + "QueueBufferingMaxKbytes must hold one maximum-size message.");
        }

        if (options.MaxInFlightRequests is < 1 or > 5)
        {
            errors.Add(prefix + "MaxInFlightRequests must be between 1 and 5.");
        }

        if (options.MessageMaxBytes is < 1_024 or > 10_485_760)
        {
            errors.Add(prefix + "MessageMaxBytes must be between 1024 and 10485760.");
        }

        if (options.MessageTimeoutMs is < 1_000 or > 120_000)
        {
            errors.Add(prefix + "MessageTimeoutMs must be between 1000 and 120000.");
        }

        if (options.LingerMs is < 0 or > 1_000)
        {
            errors.Add(prefix + "LingerMs must be between 0 and 1000.");
        }

        if (options.ShutdownFlushTimeoutMs is < 1 or > 30_000)
        {
            errors.Add(prefix + "ShutdownFlushTimeoutMs must be between 1 and 30000.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static bool ValidTopic(string? topic) =>
        !string.IsNullOrEmpty(topic)
        && topic.Length <= 249
        && topic is not "." and not ".."
        && topic.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
}
