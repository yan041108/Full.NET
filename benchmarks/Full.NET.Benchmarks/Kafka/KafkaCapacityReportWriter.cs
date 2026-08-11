using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using Full.NET.Messaging.Kafka;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 表示报告清单允许持久化的脱敏运行元数据。
/// </summary>
public sealed record KafkaCapacityManifestEvidence(
    string Scope,
    string CapacityStatus,
    string EnvironmentName,
    string BuildFingerprint,
    string RunIdHash,
    string ApprovalIdHash,
    string ClusterIdHash,
    string TopicNameHash,
    string TopicIdHash,
    string BootstrapServersHash,
    string? SaslUsernameHash,
    string SecurityProtocol,
    string? SaslMechanism,
    int ProducerLingerMilliseconds,
    int ProducerBatchSizeBytes,
    int ProducerQueueMaxMessages,
    int ProducerQueueMaxKbytes,
    int ProducerMaxInFlightRequests);

/// <summary>
/// 表示从 librdkafka Statistics JSON 投影出的数值白名单。
/// </summary>
public sealed record KafkaCapacityLibrdkafkaStatisticsEvidence(
    string SampleId,
    string Phase,
    long MessageCount,
    long MessageSizeBytes,
    long TransmittedMessages,
    long ReceivedMessages,
    long TransmittedBytes,
    long ReceivedBytes,
    long BrokerOutputQueueMessages,
    long BrokerWaitingResponseMessages,
    int ConnectedBrokerCount,
    long RequestLatencyAverageMicroseconds,
    long RequestLatencyMaximumMicroseconds,
    long ErrorCount);

/// <summary>
/// 表示一次报告写入所需的全部安全证据。
/// </summary>
public sealed record KafkaCapacityReportEvidence(
    KafkaCapacityManifestEvidence Manifest,
    IReadOnlyList<KafkaCapacitySampleEvidence> Samples,
    IReadOnlyList<KafkaCapacityLibrdkafkaStatisticsEvidence> Statistics);

/// <summary>
/// 从运行配置投影报告清单，所有连接和身份字符串仅保存 SHA-256 摘要。
/// </summary>
public static class KafkaCapacityReportProjection
{
    public static KafkaCapacityManifestEvidence CreateManifest(
        string environmentName,
        string buildFingerprint,
        string runId,
        string approvalId,
        KafkaMessagingOptions kafka,
        KafkaCapacityTopicIdentity topic)
    {
        ArgumentNullException.ThrowIfNull(kafka);
        ArgumentNullException.ThrowIfNull(topic);
        if (!Enum.TryParse<SecurityProtocol>(
                kafka.SecurityProtocol,
                ignoreCase: true,
                out var securityProtocol))
        {
            throw new InvalidDataException(
                "Kafka security protocol is invalid for report projection.");
        }

        string? saslMechanism = null;
        if (!string.IsNullOrWhiteSpace(kafka.SaslMechanism))
        {
            if (!Enum.TryParse<SaslMechanism>(
                    kafka.SaslMechanism,
                    ignoreCase: true,
                    out var parsedSaslMechanism))
            {
                throw new InvalidDataException(
                    "Kafka SASL mechanism is invalid for report projection.");
            }

            saslMechanism = parsedSaslMechanism.ToString();
        }

        return new KafkaCapacityManifestEvidence(
            "KafkaTransport",
            "Capacity-not-verified",
            Require(environmentName, nameof(environmentName)),
            Require(buildFingerprint, nameof(buildFingerprint)),
            KafkaCapacityFingerprint.Sha256(Require(runId, nameof(runId))),
            KafkaCapacityFingerprint.Sha256(
                Require(approvalId, nameof(approvalId))),
            topic.ClusterIdHash,
            KafkaCapacityFingerprint.Sha256(topic.TopicName),
            KafkaCapacityFingerprint.Sha256(topic.TopicId),
            KafkaCapacityFingerprint.Sha256(kafka.BootstrapServers ?? string.Empty),
            string.IsNullOrWhiteSpace(kafka.SaslUsername)
                ? null
                : KafkaCapacityFingerprint.Sha256(kafka.SaslUsername),
            securityProtocol.ToString(),
            saslMechanism,
            kafka.ProducerLingerMilliseconds,
            kafka.ProducerBatchSizeBytes,
            kafka.ProducerQueueMaxMessages,
            kafka.ProducerQueueMaxKbytes,
            kafka.ProducerMaxInFlightRequests);
    }

    private static string Require(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}

/// <summary>
/// 将原始 librdkafka Statistics JSON 收缩为不含名称和端点的数值白名单。
/// </summary>
public static class KafkaCapacityLibrdkafkaStatisticsProjection
{
    public static KafkaCapacityLibrdkafkaStatisticsEvidence Parse(
        string json,
        string sampleId = "unassigned",
        string phase = "unassigned")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(sampleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        long outputQueue = 0;
        long waitingResponse = 0;
        long requestLatencyTotal = 0;
        long requestLatencyMaximum = 0;
        long errorCount = 0;
        var connectedBrokers = 0;
        if (root.TryGetProperty("brokers", out var brokers)
            && brokers.ValueKind == JsonValueKind.Object)
        {
            foreach (var broker in brokers.EnumerateObject())
            {
                outputQueue += ReadInt64(broker.Value, "outbuf_cnt");
                waitingResponse += ReadInt64(broker.Value, "waitresp_cnt");
                errorCount += ReadInt64(broker.Value, "txerrs")
                    + ReadInt64(broker.Value, "rxerrs")
                    + ReadInt64(broker.Value, "req_timeouts");
                if (broker.Value.TryGetProperty("state", out var state)
                    && string.Equals(
                        state.GetString(),
                        "UP",
                        StringComparison.OrdinalIgnoreCase))
                {
                    connectedBrokers++;
                }

                if (broker.Value.TryGetProperty("rtt", out var requestLatency))
                {
                    requestLatencyTotal += ReadInt64(requestLatency, "avg");
                    requestLatencyMaximum = Math.Max(
                        requestLatencyMaximum,
                        ReadInt64(requestLatency, "max"));
                }
            }
        }

        return new KafkaCapacityLibrdkafkaStatisticsEvidence(
            sampleId,
            phase,
            ReadInt64(root, "msg_cnt"),
            ReadInt64(root, "msg_size"),
            ReadInt64(root, "txmsgs"),
            ReadInt64(root, "rxmsgs"),
            ReadInt64(root, "txbytes"),
            ReadInt64(root, "rxbytes"),
            outputQueue,
            waitingResponse,
            connectedBrokers,
            connectedBrokers == 0
                ? 0
                : requestLatencyTotal / connectedBrokers,
            requestLatencyMaximum,
            errorCount);
    }

    private static long ReadInt64(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
        && property.TryGetInt64(out var value)
            ? value
            : 0;
}

/// <summary>
/// 只从显式证据 DTO 原子写入 JSON、NDJSON 和 Markdown 工件。
/// </summary>
public static class KafkaCapacityReportWriter
{
    public static async Task WriteAsync(
        string outputDirectory,
        KafkaCapacityReportEvidence report,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(report);
        ValidateReport(report);
        var directory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);

        await WriteJsonAtomicAsync(
            Path.Combine(directory, "manifest.json"),
            report.Manifest,
            indented: true,
            cancellationToken);
        await WriteLinesAtomicAsync(
            Path.Combine(directory, "samples.ndjson"),
            report.Samples.Select(sample =>
                JsonSerializer.Serialize(sample, CompactSerializerOptions)),
            cancellationToken);
        await WriteJsonAtomicAsync(
            Path.Combine(directory, "latency-histograms.json"),
            report.Samples.Select(static sample => new
            {
                sample.SampleId,
                sample.Performance.ScheduleLatency,
                sample.Performance.AcknowledgementLatency,
                sample.Performance.EndToEndLatency,
            }),
            indented: true,
            cancellationToken);
        await WriteLinesAtomicAsync(
            Path.Combine(directory, "librdkafka-statistics.ndjson"),
            report.Statistics.Select(statistics =>
                JsonSerializer.Serialize(statistics, CompactSerializerOptions)),
            cancellationToken);

        var failureCodes = report.Samples
            .SelectMany(static sample => sample.FailureCodes)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var summary = new KafkaCapacitySummaryEvidence(
            report.Manifest.Scope,
            report.Manifest.CapacityStatus,
            report.Samples.Count,
            report.Samples.Count(static sample =>
                sample.State == KafkaCapacitySampleState.Completed),
            report.Samples.Count(static sample =>
                sample.State == KafkaCapacitySampleState.Incomplete),
            failureCodes);
        await WriteJsonAtomicAsync(
            Path.Combine(directory, "summary.json"),
            summary,
            indented: true,
            cancellationToken);
        await WriteTextAtomicAsync(
            Path.Combine(directory, "summary.md"),
            BuildMarkdown(summary),
            cancellationToken);
    }

    private static JsonSerializerOptions CompactSerializerOptions { get; } =
        CreateSerializerOptions(indented: false);

    private static JsonSerializerOptions CreateSerializerOptions(bool indented)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = indented,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static void ValidateReport(KafkaCapacityReportEvidence report)
    {
        var sampleIds = report.Samples
            .Select(static sample => sample.SampleId)
            .ToHashSet(StringComparer.Ordinal);
        var invalidFailureCode = report.Samples
            .SelectMany(static sample => sample.FailureCodes)
            .Any(static code =>
                string.IsNullOrWhiteSpace(code)
                || code.Any(static character =>
                    !(char.IsAsciiLetterOrDigit(character) || character == '_')));
        var invalidStatistics = report.Statistics.Any(statistics =>
            !sampleIds.Contains(statistics.SampleId)
            || (statistics.Phase != "initialization"
                && statistics.Phase != "warmup"
                && statistics.Phase != "measurement"
                && statistics.Phase != "drain"));
        if (!string.Equals(report.Manifest.Scope, "KafkaTransport", StringComparison.Ordinal)
            || !string.Equals(
                report.Manifest.CapacityStatus,
                "Capacity-not-verified",
                StringComparison.Ordinal)
            || report.Samples.Any(static sample =>
                !string.Equals(
                    sample.ScopeCode,
                    KafkaCapacityScopeCodes.KafkaTransport,
                    StringComparison.Ordinal))
            || invalidFailureCode
            || invalidStatistics)
        {
            throw new InvalidDataException(
                "Kafka capacity report contains a non-allowlisted value.");
        }
    }

    private static async Task WriteJsonAtomicAsync<T>(
        string path,
        T value,
        bool indented,
        CancellationToken cancellationToken) =>
        await WriteTextAtomicAsync(
            path,
            JsonSerializer.Serialize(
                value,
                CreateSerializerOptions(indented)),
            cancellationToken);

    private static async Task WriteLinesAtomicAsync(
        string path,
        IEnumerable<string> lines,
        CancellationToken cancellationToken) =>
        await WriteTextAtomicAsync(
            path,
            string.Join('\n', lines) + "\n",
            cancellationToken);

    private static async Task WriteTextAtomicAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        var temporaryPath = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string BuildMarkdown(KafkaCapacitySummaryEvidence summary)
    {
        var builder = new StringBuilder()
            .AppendLine("# Kafka capacity summary")
            .AppendLine()
            .Append("- Scope: ").AppendLine(summary.Scope)
            .Append("- CapacityStatus: ").AppendLine(summary.CapacityStatus)
            .Append("- Completed: ").AppendLine(summary.CompletedSamples.ToString())
            .Append("- Incomplete: ").AppendLine(summary.IncompleteSamples.ToString());
        foreach (var failureCode in summary.FailureCodes)
        {
            builder.Append("- Failure: ").AppendLine(failureCode);
        }

        return builder.ToString();
    }

    private sealed record KafkaCapacitySummaryEvidence(
        string Scope,
        string CapacityStatus,
        int TotalSamples,
        int CompletedSamples,
        int IncompleteSamples,
        IReadOnlyList<string> FailureCodes);
}
