namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 标识 Kafka 传输容量样本的负载形态。
/// </summary>
public enum KafkaCapacityScenario
{
    LowRate = 0,
    Throughput = 1,
}

/// <summary>
/// 保存容量证据使用的稳定范围代码。
/// </summary>
public static class KafkaCapacityScopeCodes
{
    /// <summary>
    /// 表示独立 Producer、Broker、Consumer 传输范围。
    /// </summary>
    public const string KafkaTransport = "kafka_transport";

    /// <summary>
    /// 限制进入预算、工件和客户端标识的范围码长度。
    /// </summary>
    public const int MaximumLength = 64;

    /// <summary>
    /// 校验低基数稳定机器码，防止范围进入高基数指标或不兼容工件。
    /// </summary>
    public static void Validate(string scopeCode)
    {
        if (string.IsNullOrWhiteSpace(scopeCode)
            || scopeCode.Length > MaximumLength
            || !char.IsAsciiLetter(scopeCode[0])
            || scopeCode.Any(static character =>
                !(char.IsAsciiLetterLower(character)
                    || char.IsAsciiDigit(character)
                    || character == '_')))
        {
            throw new ArgumentException(
                "Kafka capacity scope 必须是以小写 ASCII 字母开头且最长 64 字符的稳定机器码。",
                nameof(scopeCode));
        }
    }

    /// <summary>
    /// 返回兼容的人类可读范围名；ScopeCode 仍是跨版本机器契约。
    /// </summary>
    public static string GetDisplayName(string scopeCode)
    {
        Validate(scopeCode);
        return string.Equals(scopeCode, KafkaTransport, StringComparison.Ordinal)
            ? "KafkaTransport"
            : scopeCode;
    }
}

/// <summary>
/// 描述一个不可变、可指纹化的 Kafka 容量样本。
/// </summary>
public sealed record KafkaCapacitySample(
    string ScopeCode,
    string SampleId,
    KafkaCapacityScenario Scenario,
    int TargetMessagesPerSecond,
    int PayloadSizeBytes,
    int ProducerConcurrency,
    int Repetition);

/// <summary>
/// 从受限参数构建确定性的 Kafka 容量样本目录。
/// </summary>
public static class KafkaCapacityScenarioCatalog
{
    private const int MaximumSamples = 1_000;
    private const long MaximumPlannedMessages = 100_000_000;
    private static readonly TimeSpan MaximumPlannedDuration =
        TimeSpan.FromHours(24);

    /// <summary>
    /// 构建低速与吞吐样本，样本顺序不依赖运行环境。
    /// </summary>
    public static IReadOnlyList<KafkaCapacitySample> Build(
        KafkaCapacityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        KafkaCapacityScopeCodes.Validate(options.ScopeCode);
        var samples = new List<KafkaCapacitySample>();
        foreach (var scenario in options.Scenarios)
        {
            var rates = scenario == KafkaCapacityScenario.LowRate
                ? options.LowRates
                : options.ThroughputRates;
            foreach (var rate in rates)
            {
                foreach (var payloadSize in options.PayloadSizes)
                {
                    foreach (var concurrency in options.ProducerConcurrencies)
                    {
                        for (var repetition = 1;
                             repetition <= options.Repetitions;
                             repetition++)
                        {
                            var scenarioCode = scenario == KafkaCapacityScenario.LowRate
                                ? "low-rate"
                                : "throughput";
                            var sampleId = string.Create(
                                System.Globalization.CultureInfo.InvariantCulture,
                                $"{scenarioCode}-r{rate}-p{payloadSize}-c{concurrency}-n{repetition}");
                            samples.Add(new KafkaCapacitySample(
                                options.ScopeCode,
                                sampleId,
                                scenario,
                                rate,
                                payloadSize,
                                concurrency,
                                repetition));
                        }
                    }
                }
            }
        }

        return samples;
    }

    /// <summary>
    /// 验证矩阵总规模，防止合法单值组合成无界运行。
    /// </summary>
    public static void ValidatePlan(KafkaCapacityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var samples = Build(options);
        if (samples.Count > MaximumSamples)
        {
            throw new ArgumentException(
                $"Kafka capacity 样本数不得超过 {MaximumSamples}。");
        }

        var plannedMessages = samples.Sum(sample => Math.Min(
            (long)options.MaximumMessagesPerSample,
            checked((long)sample.TargetMessagesPerSecond
                * (long)options.Duration.TotalSeconds)));
        if (plannedMessages > MaximumPlannedMessages)
        {
            throw new ArgumentException(
                $"Kafka capacity 计划消息数不得超过 {MaximumPlannedMessages}。");
        }

        var plannedDuration = TimeSpan.FromTicks(checked(
            options.Duration.Ticks * samples.Count));
        if (plannedDuration > MaximumPlannedDuration)
        {
            throw new ArgumentException(
                "Kafka capacity 计划采样时长不得超过 24 小时。");
        }
    }
}
