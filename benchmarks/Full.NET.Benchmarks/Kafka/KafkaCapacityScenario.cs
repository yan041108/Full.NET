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
    public const string KafkaTransport = "kafka_transport";
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
                                KafkaCapacityScopeCodes.KafkaTransport,
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
