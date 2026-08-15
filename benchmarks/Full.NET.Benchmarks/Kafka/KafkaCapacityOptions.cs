using System.Globalization;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 定义独立 Kafka 传输容量运行的有界命令参数。
/// </summary>
public sealed record KafkaCapacityOptions
{
    public const string HelpText = """
        Kafka transport capacity runner

        Usage:
          dotnet run --project benchmarks/Full.NET.Benchmarks -c Release -- kafka-capacity [options]

        Safety:
          Dry-run is the default. Real traffic requires --execute true, configuration
          KafkaCapacity:ExecutionEnabled=true, --approval-id and --reason.

        Core options:
          --settings <path>                 UTF-8 JSON configuration (KafkaCapacity root)
          --scope <code>                    Registered driver scope (default: kafka_transport)
          --scenarios <low-rate,throughput>
          --low-rates <csv>                 1..10000 msg/s
          --throughput-rates <csv>          1..1000000 msg/s, strictly increasing
          --payload-sizes <csv>             64..1048576 bytes
          --producer-concurrency <csv>      1..256
          --partitions <n>                  1..128
          --replication-factor <n>          1..5
          --warmup-seconds <n>              0..600
          --duration-seconds <n>            1..3600
          --drain-seconds <n>               1..900
          --max-messages-per-sample <n>     1..100000000
          --repetitions <n>                 1..20
          --resume <true|false>
          --max-new-samples <n>             0..1000
          --budget <path>
          --run-id <id>
          --output <directory>
          --delete-topic <true|false>
          --execute <true|false>
          --approval-id <id>
          --reason <text>
          --host-parity-mode <fast|worker>   Scope B/C DI: Fast (default) or WorkerParity
        """;

    public bool Execute { get; init; }

    public string ScopeCode { get; init; } = KafkaCapacityScopeCodes.KafkaTransport;

    public IReadOnlyList<KafkaCapacityScenario> Scenarios { get; init; } =
        [KafkaCapacityScenario.LowRate, KafkaCapacityScenario.Throughput];

    public IReadOnlyList<int> LowRates { get; init; } = [10];

    public IReadOnlyList<int> ThroughputRates { get; init; } = [1_000];

    public IReadOnlyList<int> PayloadSizes { get; init; } = [256];

    public IReadOnlyList<int> ProducerConcurrencies { get; init; } = [1];

    public int Partitions { get; init; } = 6;

    public int ReplicationFactor { get; init; } = 1;

    public int Repetitions { get; init; } = 1;

    public TimeSpan Warmup { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public int MaximumMessagesPerSample { get; init; } = 1_000_000;

    public bool Resume { get; init; } = true;

    public int MaximumNewSamples { get; init; }

    public bool DeleteTopic { get; init; }

    public string? SettingsPath { get; init; }

    public string? BudgetPath { get; init; }

    public string? ApprovalId { get; init; }

    public string? Reason { get; init; }

    public string? RunId { get; init; }

    public string OutputDirectory { get; init; } = CreateDefaultOutputDirectory();

    public KafkaCapacityHostParityMode HostParityMode { get; init; } =
        KafkaCapacityHostParityMode.Fast;

    /// <summary>
    /// 解析命令行参数；未知参数必须失败关闭。
    /// </summary>
    public static KafkaCapacityOptions Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var values = ParsePairs(arguments);
        var scenarios = ParseScenarios(values);
        var lowRates = ParseIntList(
            values,
            "--low-rates",
            "10",
            minimum: 1,
            maximum: 10_000);
        var throughputRates = ParseIntList(
            values,
            "--throughput-rates",
            "1000",
            minimum: 1,
            maximum: 1_000_000);
        if (!throughputRates.SequenceEqual(throughputRates.Order()))
        {
            throw new ArgumentException(
                "--throughput-rates 必须严格递增。",
                "--throughput-rates");
        }

        var options = new KafkaCapacityOptions
        {
            Execute = ParseBoolean(values, "--execute", false),
            ScopeCode = values.GetValueOrDefault(
                "--scope",
                KafkaCapacityScopeCodes.KafkaTransport),
            Scenarios = scenarios,
            LowRates = lowRates,
            ThroughputRates = throughputRates,
            PayloadSizes = ParseIntList(
                values,
                "--payload-sizes",
                "256",
                minimum: 64,
                maximum: 1_048_576),
            ProducerConcurrencies = ParseIntList(
                values,
                "--producer-concurrency",
                "1",
                minimum: 1,
                maximum: 256),
            Partitions = ParseInt(
                values,
                "--partitions",
                6,
                minimum: 1,
                maximum: 128),
            ReplicationFactor = ParseInt(
                values,
                "--replication-factor",
                1,
                minimum: 1,
                maximum: 5),
            Repetitions = ParseInt(
                values,
                "--repetitions",
                1,
                minimum: 1,
                maximum: 20),
            Warmup = TimeSpan.FromSeconds(ParseInt(
                values,
                "--warmup-seconds",
                10,
                minimum: 0,
                maximum: 600)),
            Duration = TimeSpan.FromSeconds(ParseInt(
                values,
                "--duration-seconds",
                30,
                minimum: 1,
                maximum: 3_600)),
            DrainTimeout = TimeSpan.FromSeconds(ParseInt(
                values,
                "--drain-seconds",
                60,
                minimum: 1,
                maximum: 900)),
            MaximumMessagesPerSample = ParseInt(
                values,
                "--max-messages-per-sample",
                1_000_000,
                minimum: 1,
                maximum: 100_000_000),
            Resume = ParseBoolean(values, "--resume", true),
            MaximumNewSamples = ParseInt(
                values,
                "--max-new-samples",
                0,
                minimum: 0,
                maximum: 1_000),
            DeleteTopic = ParseBoolean(values, "--delete-topic", false),
            SettingsPath = GetOptional(values, "--settings"),
            BudgetPath = GetOptional(values, "--budget"),
            ApprovalId = GetOptional(values, "--approval-id"),
            Reason = GetOptional(values, "--reason"),
            RunId = GetOptional(values, "--run-id"),
            OutputDirectory = values.GetValueOrDefault(
                "--output",
                CreateDefaultOutputDirectory()),
            HostParityMode = ParseHostParityMode(values),
        };
        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
        {
            throw new ArgumentException("--output 不能为空。", "--output");
        }

        KafkaCapacityScopeCodes.Validate(options.ScopeCode);

        if (options.MaximumNewSamples > 0 && !options.Resume)
        {
            throw new ArgumentException(
                "--max-new-samples 大于 0 时必须启用 --resume。");
        }

        KafkaCapacityScenarioCatalog.ValidatePlan(options);
        return options;
    }

    private static Dictionary<string, string> ParsePairs(
        IReadOnlyList<string> arguments)
    {
        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < arguments.Count; index += 2)
        {
            var key = arguments[index];
            if (!KnownOptions.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"未知参数：{key}");
            }

            if (index + 1 >= arguments.Count)
            {
                throw new ArgumentException($"参数 {key} 缺少值。");
            }

            if (!values.TryAdd(key, arguments[index + 1]))
            {
                throw new ArgumentException($"参数 {key} 不能重复。");
            }
        }

        return values;
    }

    private static IReadOnlyList<KafkaCapacityScenario> ParseScenarios(
        IReadOnlyDictionary<string, string> values)
    {
        var tokens = Split(values.GetValueOrDefault(
            "--scenarios",
            "low-rate,throughput"));
        var scenarios = tokens.Select(token => token.ToLowerInvariant() switch
        {
            "low-rate" => KafkaCapacityScenario.LowRate,
            "throughput" => KafkaCapacityScenario.Throughput,
            _ => throw new ArgumentException(
                "--scenarios 只能包含 low-rate 和 throughput。",
                "--scenarios"),
        }).ToArray();
        EnsureDistinct(scenarios, "--scenarios");
        return scenarios;
    }

    private static IReadOnlyList<int> ParseIntList(
        IReadOnlyDictionary<string, string> values,
        string key,
        string defaultValue,
        int minimum,
        int maximum)
    {
        var tokens = Split(values.GetValueOrDefault(key, defaultValue));
        var result = tokens.Select(token =>
        {
            if (!int.TryParse(
                    token,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var value)
                || value < minimum
                || value > maximum)
            {
                throw new ArgumentOutOfRangeException(
                    key,
                    token,
                    $"{key} 必须位于 {minimum}..{maximum}。");
            }

            return value;
        }).ToArray();
        EnsureDistinct(result, key);
        return result;
    }

    private static int ParseInt(
        IReadOnlyDictionary<string, string> values,
        string key,
        int defaultValue,
        int minimum,
        int maximum)
    {
        if (!values.TryGetValue(key, out var raw))
        {
            return defaultValue;
        }

        if (!int.TryParse(
                raw,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value)
            || value < minimum
            || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                key,
                raw,
                $"{key} 必须位于 {minimum}..{maximum}。");
        }

        return value;
    }

    private static bool ParseBoolean(
        IReadOnlyDictionary<string, string> values,
        string key,
        bool defaultValue)
    {
        if (!values.TryGetValue(key, out var raw))
        {
            return defaultValue;
        }

        if (!bool.TryParse(raw, out var value))
        {
            throw new ArgumentException($"{key} 必须是 true 或 false。", key);
        }

        return value;
    }

    private static string? GetOptional(
        IReadOnlyDictionary<string, string> values,
        string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{key} 不能为空。", key);
        }

        return value;
    }

    private static string[] Split(string value)
    {
        var tokens = value.Split(
            ',',
            StringSplitOptions.TrimEntries);
        if (tokens.Length == 0
            || tokens.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "列表参数不能包含空值。");
        }

        return tokens;
    }

    private static void EnsureDistinct<T>(
        IReadOnlyCollection<T> values,
        string key)
    {
        if (values.Distinct().Count() != values.Count)
        {
            throw new ArgumentException($"{key} 不能包含重复值。", key);
        }
    }

    private static KafkaCapacityHostParityMode ParseHostParityMode(
        IReadOnlyDictionary<string, string> values)
    {
        if (!values.TryGetValue("--host-parity-mode", out var raw))
        {
            return KafkaCapacityHostParityMode.Fast;
        }

        return raw.ToLowerInvariant() switch
        {
            "fast" => KafkaCapacityHostParityMode.Fast,
            "worker" or "workerparity" => KafkaCapacityHostParityMode.WorkerParity,
            _ => throw new ArgumentException(
                "--host-parity-mode 必须是 fast 或 worker。",
                "--host-parity-mode"),
        };
    }

    private static readonly string[] KnownOptions =
    [
        "--scenarios",
        "--scope",
        "--low-rates",
        "--throughput-rates",
        "--payload-sizes",
        "--producer-concurrency",
        "--partitions",
        "--replication-factor",
        "--repetitions",
        "--warmup-seconds",
        "--duration-seconds",
        "--drain-seconds",
        "--max-messages-per-sample",
        "--resume",
        "--max-new-samples",
        "--delete-topic",
        "--execute",
        "--settings",
        "--budget",
        "--approval-id",
        "--reason",
        "--run-id",
        "--output",
        "--host-parity-mode",
    ];

    private static string CreateDefaultOutputDirectory() => Path.Combine(
        "BenchmarkDotNet.Artifacts",
        "kafka-capacity",
        string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"));
}
