using System.Globalization;

namespace Full.NET.Benchmarks.Jobs;

public enum JobsBacklogQueryBenchmarkMode
{
    Baseline = 0,
    IndexAb = 1,
}

public sealed record JobsBacklogQueryBenchmarkOptions(
    int Rows,
    int WarmupIterations,
    int MeasurementIterations,
    int MutationIterations,
    int Concurrency,
    DateTimeOffset ReferenceUtc,
    string OutputDirectory,
    IReadOnlyList<string> Providers,
    JobsBacklogQueryBenchmarkMode Mode)
{
    public const string HelpText =
        """
        Full.NET Jobs 积压查询双库基准

        用法：
          dotnet run --project benchmarks/Full.NET.Benchmarks -c Release -- jobs-backlog-query [选项]

        选项：
          --rows <n>           数据行数，默认 100000，范围 1000..1000000 且必须为 20 的倍数
          --warmup <n>         baseline 每 Provider、index-ab 每 Variant 预热次数，默认 5，范围 1..100
          --iterations <n>     baseline 每 Provider、index-ab 每 Variant 采样次数，默认 30，范围 5..1000
          --mutation-iterations <n>
                               index-ab 每 Variant/每类写路径采样次数，默认 10，范围 3..100
          --mode <value>       baseline 或 index-ab，默认 baseline
          --providers <list>   sqlserver,mysql 的逗号分隔子集，默认两者
          --reference-utc <v>  观测 UTC，默认 2026-07-30T00:00:00Z
          --output <path>      工件目录，默认 BenchmarkDotNet.Artifacts/jobs-backlog-query/<UTC>
          --help               显示帮助

        index-ab 候选索引：
          IX_fn_jobs_execution_BacklogStatusTenant

        index-ab 写路径门禁：
          trigger_insert、claim、terminal_success 的 P95 回归均不得超过 20%
        """;

    public static JobsBacklogQueryBenchmarkOptions Parse(
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var values = ParsePairs(arguments);
        var rows = ParseInt(values, "--rows", 100_000, 1_000, 1_000_000);
        if (rows % JobsBacklogDataset.BucketCount != 0)
        {
            throw new ArgumentException(
                $"--rows 必须为 {JobsBacklogDataset.BucketCount} 的倍数。");
        }

        var warmup = ParseInt(values, "--warmup", 5, 1, 100);
        var iterations = ParseInt(values, "--iterations", 30, 5, 1_000);
        var mutationIterations = ParseInt(
            values,
            "--mutation-iterations",
            10,
            3,
            100);
        var mode = ParseMode(values);
        var referenceUtc = ParseReferenceUtc(values);
        var providers = ParseProviders(values);
        var outputDirectory = values.GetValueOrDefault(
            "--output",
            Path.Combine(
                "BenchmarkDotNet.Artifacts",
                "jobs-backlog-query",
                DateTimeOffset.UtcNow.ToString(
                    "yyyyMMdd-HHmmss",
                    CultureInfo.InvariantCulture)));

        return new JobsBacklogQueryBenchmarkOptions(
            rows,
            warmup,
            iterations,
            mutationIterations,
            Concurrency: 1,
            referenceUtc,
            outputDirectory,
            providers,
            mode);
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
                NumberStyles.None,
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

    private static DateTimeOffset ParseReferenceUtc(
        IReadOnlyDictionary<string, string> values)
    {
        if (!values.TryGetValue("--reference-utc", out var raw))
        {
            return new DateTimeOffset(
                2026,
                7,
                30,
                0,
                0,
                0,
                TimeSpan.Zero);
        }

        if (!DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal
                    | DateTimeStyles.AdjustToUniversal,
                out var value))
        {
            throw new ArgumentException(
                "--reference-utc 必须是有效的 UTC 时间。");
        }

        return value;
    }

    private static IReadOnlyList<string> ParseProviders(
        IReadOnlyDictionary<string, string> values)
    {
        var rawProviders = values
            .GetValueOrDefault("--providers", "sqlserver,mysql")
            .Split(
                ',',
                StringSplitOptions.TrimEntries
                    | StringSplitOptions.RemoveEmptyEntries);
        if (rawProviders.Length == 0
            || rawProviders.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != rawProviders.Length)
        {
            throw new ArgumentException(
                "--providers 不得为空或包含重复 Provider。");
        }

        var providers = rawProviders
            .Select(provider => provider.ToLowerInvariant())
            .ToArray();
        if (providers.Any(provider =>
                provider is not ("sqlserver" or "mysql")))
        {
            throw new ArgumentException(
                "--providers 只能包含 sqlserver 和 mysql。");
        }

        return providers;
    }

    private static JobsBacklogQueryBenchmarkMode ParseMode(
        IReadOnlyDictionary<string, string> values) =>
        values.GetValueOrDefault("--mode", "baseline")
            .ToLowerInvariant() switch
        {
            "baseline" => JobsBacklogQueryBenchmarkMode.Baseline,
            "index-ab" => JobsBacklogQueryBenchmarkMode.IndexAb,
            _ => throw new ArgumentException(
                "--mode 只能是 baseline 或 index-ab。"),
        };

    private static readonly string[] KnownOptions =
    [
        "--rows",
        "--warmup",
        "--iterations",
        "--mutation-iterations",
        "--mode",
        "--providers",
        "--reference-utc",
        "--output",
    ];
}
