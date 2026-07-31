using System.Globalization;

namespace Full.NET.Benchmarks.Jobs;

public sealed record JobsCapacityOptions(
    IReadOnlyList<string> Providers,
    IReadOnlyList<int> ConcurrencyLevels,
    IReadOnlyList<int> HandlerDelayMilliseconds,
    IReadOnlyList<int> ReplicaCounts,
    int Repetitions,
    TimeSpan Warmup,
    TimeSpan Duration,
    int SeedJobs,
    int BatchSize,
    int HandlerKeyCount,
    int FailingHandlerKeyCount,
    TimeSpan Lease,
    TimeSpan LeaseRenewal,
    bool ResumeEnabled,
    int MaximumNewSamples,
    string OutputDirectory)
{
    public const string HelpText =
        """
        Full.NET Jobs 并发容量矩阵

        用法：
          dotnet run --project benchmarks/Full.NET.Benchmarks -c Release -- jobs-capacity [选项]

        选项：
          --providers <list>              sqlserver,mysql 的逗号分隔子集，默认两者
          --concurrency <list>            单副本并发度，默认 1,2,4,8
          --handler-delay-ms <list>       Handler 延迟毫秒，默认 0,1000
          --replicas <list>               Worker 副本数，默认 1,2
          --repetitions <n>               每个场景重复次数，默认 3
          --warmup-seconds <n>            每个场景预热秒数，默认 10
          --duration-seconds <n>          每个场景采样秒数，默认 30
          --seed-jobs <n>                 最少预置任务数，默认 20000
          --batch-size <n>                单次领取数量，默认 16
          --handler-keys <n>              固定 Handler Key 数，默认 8
          --failing-handler-keys <n>      预期失败 Handler Key 数，默认 1
          --lease-seconds <n>             租约秒数，默认 30
          --lease-renewal-seconds <n>     续租周期秒数，默认 5
          --resume <bool>                 是否从同目录断点续跑，默认 true
          --max-new-samples <n>           本轮最多新增样本，0 表示不限制
          --output <path>                 工件目录
          --help                          显示帮助

        完整矩阵只用于手工 CI；本地开发应缩短列表、时长并设置 repetitions=1。
        """;

    public static JobsCapacityOptions Parse(
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var values = ParsePairs(arguments);
        var providers = ParseProviders(values);
        var concurrency = ParseList(
            values,
            "--concurrency",
            "1,2,4,8",
            minimum: 1,
            maximum: 16);
        var delays = ParseList(
            values,
            "--handler-delay-ms",
            "0,1000",
            minimum: 0,
            maximum: 60_000);
        var replicas = ParseList(
            values,
            "--replicas",
            "1,2",
            minimum: 1,
            maximum: 16);
        var repetitions = ParseInt(
            values,
            "--repetitions",
            defaultValue: 3,
            minimum: 1,
            maximum: 20);
        var warmupSeconds = ParseInt(
            values,
            "--warmup-seconds",
            defaultValue: 10,
            minimum: 1,
            maximum: 3600);
        var durationSeconds = ParseInt(
            values,
            "--duration-seconds",
            defaultValue: 30,
            minimum: 1,
            maximum: 86_400);
        var seedJobs = ParseInt(
            values,
            "--seed-jobs",
            defaultValue: 20_000,
            minimum: 1,
            maximum: 1_000_000);
        var batchSize = ParseInt(
            values,
            "--batch-size",
            defaultValue: 16,
            minimum: 1,
            maximum: 50);
        var handlerKeyCount = ParseInt(
            values,
            "--handler-keys",
            defaultValue: 8,
            minimum: 2,
            maximum: 50);
        var failingHandlerKeyCount = ParseInt(
            values,
            "--failing-handler-keys",
            defaultValue: 1,
            minimum: 1,
            maximum: 49);
        var leaseSeconds = ParseInt(
            values,
            "--lease-seconds",
            defaultValue: 30,
            minimum: 30,
            maximum: 3600);
        var leaseRenewalSeconds = ParseInt(
            values,
            "--lease-renewal-seconds",
            defaultValue: 5,
            minimum: 5,
            maximum: 1200);
        var resume = ParseBoolean(values, "--resume", defaultValue: true);
        var maximumNewSamples = ParseInt(
            values,
            "--max-new-samples",
            defaultValue: 0,
            minimum: 0,
            maximum: 10_000);

        if (concurrency.Max() > batchSize)
        {
            throw new ArgumentException(
                "--concurrency 的最大值不得超过 --batch-size。");
        }

        if (handlerKeyCount > batchSize
            || failingHandlerKeyCount >= handlerKeyCount)
        {
            throw new ArgumentException(
                "--failing-handler-keys 必须小于 --handler-keys，且 Handler Key 总数不得超过批量。");
        }

        var minimumSeed = checked(batchSize * replicas.Max() * 2);
        if (seedJobs < minimumSeed)
        {
            throw new ArgumentException(
                $"--seed-jobs 不得小于批量与最大副本预留量 {minimumSeed}。");
        }

        if (leaseRenewalSeconds > leaseSeconds / 2)
        {
            throw new ArgumentException(
                "--lease-renewal-seconds 不得超过租约的一半。");
        }

        if (maximumNewSamples > 0 && !resume)
        {
            throw new ArgumentException(
                "--max-new-samples 大于 0 时必须启用 --resume。");
        }

        var outputDirectory = values.GetValueOrDefault(
            "--output",
            Path.Combine(
                "BenchmarkDotNet.Artifacts",
                "jobs-capacity",
                DateTimeOffset.UtcNow.ToString(
                    "yyyyMMdd-HHmmss",
                    CultureInfo.InvariantCulture)));
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("--output 不能为空。", "--output");
        }

        return new JobsCapacityOptions(
            providers,
            concurrency,
            delays,
            replicas,
            repetitions,
            TimeSpan.FromSeconds(warmupSeconds),
            TimeSpan.FromSeconds(durationSeconds),
            seedJobs,
            batchSize,
            handlerKeyCount,
            failingHandlerKeyCount,
            TimeSpan.FromSeconds(leaseSeconds),
            TimeSpan.FromSeconds(leaseRenewalSeconds),
            resume,
            maximumNewSamples,
            outputDirectory);
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
                throw new ArgumentException($"参数 {key} 不得重复。");
            }
        }

        return values;
    }

    private static IReadOnlyList<string> ParseProviders(
        IReadOnlyDictionary<string, string> values)
    {
        var providers = values.GetValueOrDefault(
                "--providers",
                "sqlserver,mysql")
            .Split(
                ',',
                StringSplitOptions.TrimEntries
                    | StringSplitOptions.RemoveEmptyEntries)
            .Select(provider => provider.ToLowerInvariant())
            .ToArray();
        if (providers.Length == 0
            || providers.Distinct(StringComparer.Ordinal).Count()
                != providers.Length
            || providers.Any(provider =>
                provider is not ("sqlserver" or "mysql")))
        {
            throw new ArgumentException(
                "--providers 只能包含不重复的 sqlserver 和 mysql。");
        }

        return providers;
    }

    private static IReadOnlyList<int> ParseList(
        IReadOnlyDictionary<string, string> values,
        string key,
        string defaultValue,
        int minimum,
        int maximum)
    {
        var tokens = values.GetValueOrDefault(key, defaultValue)
            .Split(
                ',',
                StringSplitOptions.TrimEntries
                    | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            throw new ArgumentException($"{key} 至少需要一个值。", key);
        }

        var result = new int[tokens.Length];
        for (var index = 0; index < tokens.Length; index++)
        {
            if (!int.TryParse(
                    tokens[index],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var value)
                || value < minimum
                || value > maximum)
            {
                throw new ArgumentOutOfRangeException(
                    key,
                    tokens[index],
                    $"{key} 必须位于 {minimum}..{maximum}。");
            }

            result[index] = value;
        }

        if (result.Distinct().Count() != result.Length)
        {
            throw new ArgumentException($"{key} 不得包含重复值。", key);
        }

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
            throw new ArgumentException(
                $"{key} 必须是 true 或 false。",
                key);
        }

        return value;
    }

    private static readonly string[] KnownOptions =
    [
        "--providers",
        "--concurrency",
        "--handler-delay-ms",
        "--replicas",
        "--repetitions",
        "--warmup-seconds",
        "--duration-seconds",
        "--seed-jobs",
        "--batch-size",
        "--handler-keys",
        "--failing-handler-keys",
        "--lease-seconds",
        "--lease-renewal-seconds",
        "--resume",
        "--max-new-samples",
        "--output",
    ];
}
