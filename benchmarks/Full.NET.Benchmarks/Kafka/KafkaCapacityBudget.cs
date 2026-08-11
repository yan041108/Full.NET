using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 表示单个样本的性能预算门禁结果。
/// </summary>
public sealed record KafkaCapacityBudgetAssessment(
    bool Passed,
    IReadOnlyList<string> FailureCodes);

/// <summary>
/// 定义与环境、集群、基线和场景精确绑定的可选性能预算。
/// </summary>
public sealed class KafkaCapacityBudget
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string EnvironmentName { get; set; } = string.Empty;

    public string ClusterIdHash { get; set; } = string.Empty;

    public string BaselineGitCommit { get; set; } = string.Empty;

    public DateTimeOffset GeneratedAtUtc { get; set; }

    public IReadOnlyList<KafkaCapacityBudgetEntry> Entries { get; set; } = [];

    public static async Task<KafkaCapacityBudget> LoadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        await using var stream = File.OpenRead(fullPath);
        var budget = await JsonSerializer.DeserializeAsync<KafkaCapacityBudget>(
            stream,
            SerializerOptions,
            cancellationToken)
            ?? throw new InvalidDataException("Kafka capacity budget is empty.");
        budget.Validate();
        return budget;
    }

    public KafkaCapacityBudgetAssessment Assess(
        string environmentName,
        string clusterIdHash,
        string baselineGitCommit,
        KafkaCapacitySampleEvidence sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (!string.Equals(
                EnvironmentName,
                environmentName,
                StringComparison.Ordinal)
            || !string.Equals(
                ClusterIdHash,
                clusterIdHash,
                StringComparison.Ordinal)
            || !string.Equals(
                BaselineGitCommit,
                baselineGitCommit,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Kafka capacity budget environment, cluster or baseline does not match this run.");
        }

        var matches = Entries.Where(entry => entry.Matches(sample)).ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException(
                "Kafka capacity budget must contain exactly one matching scenario entry.");
        }

        var entry = matches[0];
        var performance = sample.Performance;
        var failures = new List<string>();
        CheckMinimum(
            performance.ScheduledMessagesPerSecond,
            entry.MinimumScheduledMessagesPerSecond,
            "scheduled_rate_budget_not_met",
            failures);
        CheckMinimum(
            performance.AcknowledgedMessagesPerSecond,
            entry.MinimumAcknowledgedMessagesPerSecond,
            "acknowledged_rate_budget_not_met",
            failures);
        CheckMinimum(
            performance.ConsumedMessagesPerSecond,
            entry.MinimumConsumedMessagesPerSecond,
            "consumed_rate_budget_not_met",
            failures);
        CheckMaximum(
            performance.ScheduleLatency.P95Microseconds,
            entry.MaximumScheduleP95Microseconds,
            "schedule_p95_budget_exceeded",
            failures);
        CheckMaximum(
            performance.ScheduleLatency.P99Microseconds,
            entry.MaximumScheduleP99Microseconds,
            "schedule_p99_budget_exceeded",
            failures);
        CheckMaximum(
            performance.AcknowledgementLatency.P95Microseconds,
            entry.MaximumAcknowledgementP95Microseconds,
            "acknowledgement_p95_budget_exceeded",
            failures);
        CheckMaximum(
            performance.AcknowledgementLatency.P99Microseconds,
            entry.MaximumAcknowledgementP99Microseconds,
            "acknowledgement_p99_budget_exceeded",
            failures);
        CheckMaximum(
            performance.EndToEndLatency.P95Microseconds,
            entry.MaximumEndToEndP95Microseconds,
            "end_to_end_p95_budget_exceeded",
            failures);
        CheckMaximum(
            performance.EndToEndLatency.P99Microseconds,
            entry.MaximumEndToEndP99Microseconds,
            "end_to_end_p99_budget_exceeded",
            failures);
        CheckMaximum(
            performance.DrainMilliseconds,
            entry.MaximumDrainMilliseconds,
            "drain_budget_exceeded",
            failures);
        CheckMaximum(
            performance.CpuPercent,
            entry.MaximumCpuPercent,
            "cpu_budget_exceeded",
            failures);
        CheckMaximum(
            performance.ManagedHeapBytes,
            entry.MaximumManagedHeapBytes,
            "managed_heap_budget_exceeded",
            failures);
        CheckMaximum(
            performance.LocalQueueMessages,
            entry.MaximumLocalQueueMessages,
            "local_queue_budget_exceeded",
            failures);
        return new KafkaCapacityBudgetAssessment(failures.Count == 0, failures);
    }

    private static JsonSerializerOptions SerializerOptions { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion
            || string.IsNullOrWhiteSpace(EnvironmentName)
            || string.IsNullOrWhiteSpace(ClusterIdHash)
            || string.IsNullOrWhiteSpace(BaselineGitCommit)
            || GeneratedAtUtc == default
            || GeneratedAtUtc.Offset != TimeSpan.Zero
            || Entries.Count == 0)
        {
            throw new InvalidDataException(
                "Kafka capacity budget schema or required values are invalid.");
        }

        foreach (var entry in Entries)
        {
            entry.Validate();
        }

        var distinctKeys = Entries.Select(static entry => entry.Key)
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (distinctKeys != Entries.Count)
        {
            throw new InvalidDataException(
                "Kafka capacity budget contains duplicate scenario entries.");
        }
    }

    private static void CheckMinimum(
        double actual,
        double? expected,
        string failureCode,
        ICollection<string> failures)
    {
        if (expected.HasValue && actual < expected.Value)
        {
            failures.Add(failureCode);
        }
    }

    private static void CheckMaximum(
        double actual,
        double? expected,
        string failureCode,
        ICollection<string> failures)
    {
        if (expected.HasValue && actual > expected.Value)
        {
            failures.Add(failureCode);
        }
    }
}

/// <summary>
/// 定义一个完整场景键及其可选性能阈值。
/// </summary>
public sealed class KafkaCapacityBudgetEntry
{
    public string ScopeCode { get; set; } = string.Empty;

    public KafkaCapacityScenario Scenario { get; set; }

    public int TargetMessagesPerSecond { get; set; }

    public int PayloadSizeBytes { get; set; }

    public int Partitions { get; set; }

    public int ProducerConcurrency { get; set; }

    public double? MinimumScheduledMessagesPerSecond { get; set; }

    public double? MinimumAcknowledgedMessagesPerSecond { get; set; }

    public double? MinimumConsumedMessagesPerSecond { get; set; }

    public long? MaximumScheduleP99Microseconds { get; set; }

    public long? MaximumScheduleP95Microseconds { get; set; }

    public long? MaximumAcknowledgementP95Microseconds { get; set; }

    public long? MaximumAcknowledgementP99Microseconds { get; set; }

    public long? MaximumEndToEndP95Microseconds { get; set; }

    public long? MaximumEndToEndP99Microseconds { get; set; }

    public long? MaximumDrainMilliseconds { get; set; }

    public double? MaximumCpuPercent { get; set; }

    public long? MaximumManagedHeapBytes { get; set; }

    public long? MaximumLocalQueueMessages { get; set; }

    internal string Key =>
        $"{ScopeCode}|{Scenario}|{TargetMessagesPerSecond}|{PayloadSizeBytes}|{Partitions}|{ProducerConcurrency}";

    internal bool Matches(KafkaCapacitySampleEvidence sample) =>
        string.Equals(ScopeCode, sample.ScopeCode, StringComparison.Ordinal)
        && Scenario == sample.Scenario
        && TargetMessagesPerSecond == sample.TargetMessagesPerSecond
        && PayloadSizeBytes == sample.PayloadSizeBytes
        && Partitions == sample.Partitions
        && ProducerConcurrency == sample.ProducerConcurrency;

    internal void Validate()
    {
        var thresholds = new double?[]
        {
            MinimumScheduledMessagesPerSecond,
            MinimumAcknowledgedMessagesPerSecond,
            MinimumConsumedMessagesPerSecond,
            MaximumScheduleP95Microseconds,
            MaximumScheduleP99Microseconds,
            MaximumAcknowledgementP95Microseconds,
            MaximumAcknowledgementP99Microseconds,
            MaximumEndToEndP95Microseconds,
            MaximumEndToEndP99Microseconds,
            MaximumDrainMilliseconds,
            MaximumCpuPercent,
            MaximumManagedHeapBytes,
            MaximumLocalQueueMessages,
        };
        if (!string.Equals(
                ScopeCode,
                KafkaCapacityScopeCodes.KafkaTransport,
                StringComparison.Ordinal)
            || !Enum.IsDefined(Scenario)
            || TargetMessagesPerSecond <= 0
            || PayloadSizeBytes < KafkaCapacityEnvelopeCodec.MinimumPayloadSizeBytes
            || Partitions <= 0
            || ProducerConcurrency <= 0
            || thresholds.All(static threshold => !threshold.HasValue)
            || thresholds.Any(static threshold => threshold is <= 0))
        {
            throw new InvalidDataException(
                "Kafka capacity budget entry is invalid.");
        }
    }
}
