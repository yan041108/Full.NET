using System.Globalization;

namespace Full.NET.Benchmarks.MixedLoad;

public sealed record MixedLoadOptions(
    IReadOnlyList<string> Providers,
    IReadOnlyList<int> ConcurrencyLevels,
    TimeSpan Warmup,
    TimeSpan Duration,
    int Seed,
    double MaximumUnexpectedErrorRate,
    string OutputDirectory)
{
    public const string HelpText =
        """
        Full.NET 生产等价混合负载基准

        用法：
          dotnet run --project benchmarks/Full.NET.Benchmarks -c Release -- mixed-load [选项]

        选项：
          --providers <list>       sqlserver,mysql 的逗号分隔子集，默认两者
          --concurrency <list>     并发矩阵，默认 1,4,16,32
          --warmup-seconds <n>     每个并发档预热秒数，默认 30
          --duration-seconds <n>   每个并发档采样秒数，默认 600
          --seed <n>               场景选择固定种子，默认 20260728
          --max-error-rate <value> 非预期错误率上限，默认 0.005
          --output <path>          工件目录，默认 BenchmarkDotNet.Artifacts/mixed-load/<UTC>
          --help                   显示帮助
        """;

    public static MixedLoadOptions Parse(IReadOnlyList<string> arguments)
    {
        var values = ParsePairs(arguments);
        var providers = ParseProviders(values);
        var concurrency = ParseConcurrency(values);
        var warmupSeconds = ParsePositiveInt(
            values,
            "--warmup-seconds",
            defaultValue: 30,
            minimum: 0);
        var durationSeconds = ParsePositiveInt(
            values,
            "--duration-seconds",
            defaultValue: 600,
            minimum: 1);
        var seed = ParseInt(values, "--seed", 20260728);
        var maximumErrorRate = ParseErrorRate(values);
        var outputDirectory = values.GetValueOrDefault(
            "--output",
            Path.Combine(
                "BenchmarkDotNet.Artifacts",
                "mixed-load",
                DateTimeOffset.UtcNow.ToString(
                    "yyyyMMdd-HHmmss",
                    CultureInfo.InvariantCulture)));

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("--output 不能为空。", "--output");
        }

        return new MixedLoadOptions(
            providers,
            concurrency,
            TimeSpan.FromSeconds(warmupSeconds),
            TimeSpan.FromSeconds(durationSeconds),
            seed,
            maximumErrorRate,
            outputDirectory);
    }

    private static Dictionary<string, string> ParsePairs(
        IReadOnlyList<string> arguments)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

    private static IReadOnlyList<string> ParseProviders(
        IReadOnlyDictionary<string, string> values)
    {
        var providers = values.GetValueOrDefault("--providers", "sqlserver,mysql")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(provider => provider.ToLowerInvariant())
            .ToArray();
        if (providers.Length == 0
            || providers.Distinct(StringComparer.Ordinal).Count() != providers.Length
            || providers.Any(provider => provider is not ("sqlserver" or "mysql")))
        {
            throw new ArgumentException(
                "--providers 只能包含不重复的 sqlserver 和 mysql。");
        }

        return providers;
    }

    private static IReadOnlyList<int> ParseConcurrency(
        IReadOnlyDictionary<string, string> values)
    {
        var rawValues = values.GetValueOrDefault("--concurrency", "1,4,16,32")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (rawValues.Length == 0)
        {
            throw new ArgumentException("--concurrency 至少需要一个并发值。");
        }

        var result = new int[rawValues.Length];
        for (var index = 0; index < rawValues.Length; index++)
        {
            if (!int.TryParse(
                    rawValues[index],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var value)
                || value is < 1 or > 256)
            {
                throw new ArgumentOutOfRangeException(
                    "--concurrency",
                    rawValues[index],
                    "并发值必须位于 1..256。");
            }

            result[index] = value;
        }

        if (result.Distinct().Count() != result.Length)
        {
            throw new ArgumentException("--concurrency 不能包含重复值。");
        }

        return result;
    }

    private static int ParsePositiveInt(
        IReadOnlyDictionary<string, string> values,
        string key,
        int defaultValue,
        int minimum)
    {
        var value = ParseInt(values, key, defaultValue);
        if (value < minimum)
        {
            throw new ArgumentOutOfRangeException(
                key,
                value,
                $"{key} 必须大于或等于 {minimum}。");
        }

        return value;
    }

    private static int ParseInt(
        IReadOnlyDictionary<string, string> values,
        string key,
        int defaultValue)
    {
        if (!values.TryGetValue(key, out var raw))
        {
            return defaultValue;
        }

        if (!int.TryParse(
                raw,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value))
        {
            throw new ArgumentException($"{key} 必须是整数。", key);
        }

        return value;
    }

    private static double ParseErrorRate(
        IReadOnlyDictionary<string, string> values)
    {
        if (!values.TryGetValue("--max-error-rate", out var raw))
        {
            return 0.005d;
        }

        if (!double.TryParse(
                raw,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value)
            || value is < 0d or >= 1d)
        {
            throw new ArgumentOutOfRangeException(
                "--max-error-rate",
                raw,
                "--max-error-rate 必须位于 0（含）到 1（不含）之间。");
        }

        return value;
    }

    private static readonly string[] KnownOptions =
    [
        "--providers",
        "--concurrency",
        "--warmup-seconds",
        "--duration-seconds",
        "--seed",
        "--max-error-rate",
        "--output",
    ];
}
