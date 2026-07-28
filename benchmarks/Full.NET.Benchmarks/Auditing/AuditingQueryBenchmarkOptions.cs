using System.Globalization;

namespace Full.NET.Benchmarks.Auditing;

public enum AuditingQueryBenchmarkMode
{
    Baseline = 0,
    SqlServerPlanAb = 1,
    MySqlIndexAb = 2,
    MySqlLateMaterializationAb = 3,
    CursorAb = 4,
}

public sealed record AuditingQueryBenchmarkOptions(
    int Rows,
    int WarmupIterations,
    int MeasurementIterations,
    int PageSize,
    int Concurrency,
    DateTimeOffset ReferenceUtc,
    string OutputDirectory,
    IReadOnlyList<string> Providers,
    AuditingQueryBenchmarkMode Mode)
{
    public const string HelpText =
        """
        Full.NET 审计大表查询基准

        用法：
          dotnet run --project benchmarks/Full.NET.Benchmarks -c Release -- audit-query [选项]

        选项：
          --rows <n>           数据行数，默认 100000，最小 1000
          --warmup <n>         每场景预热次数，默认 5，最小 1
          --iterations <n>     每场景采样次数，默认 30，最小 5
          --page-size <n>      页面大小，默认 50，范围 1..500
          --providers <list>   sqlserver,mysql 的逗号分隔子集，默认两者
          --mode <value>       baseline、sqlserver-plan-ab、mysql-index-ab、mysql-late-materialization-ab 或 cursor-ab，默认 baseline
          --reference-utc <v>  数据集结束 UTC，默认 2026-07-27T00:00:00Z
          --output <path>      工件目录，默认 BenchmarkDotNet.Artifacts/auditing-query/<UTC>
          --help               显示帮助
        """;

    public static AuditingQueryBenchmarkOptions Parse(IReadOnlyList<string> arguments)
    {
        var values = ParsePairs(arguments);
        var rows = ParseInt(values, "--rows", 100_000, minimum: 1_000);
        var warmup = ParseInt(values, "--warmup", 5, minimum: 1);
        var iterations = ParseInt(values, "--iterations", 30, minimum: 5);
        var pageSize = ParseInt(values, "--page-size", 50, minimum: 1, maximum: 500);
        var referenceUtc = ParseReferenceUtc(values);
        var providers = ParseProviders(values);
        var mode = ParseMode(values);
        if (mode == AuditingQueryBenchmarkMode.SqlServerPlanAb
            && (providers.Count != 1
                || !string.Equals(
                    providers[0],
                    "sqlserver",
                    StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "sqlserver-plan-ab 模式只接受 --providers sqlserver。");
        }
        if (mode is AuditingQueryBenchmarkMode.MySqlIndexAb
                or AuditingQueryBenchmarkMode.MySqlLateMaterializationAb
            && (providers.Count != 1
                || !string.Equals(
                    providers[0],
                    "mysql",
                    StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "MySQL A/B 模式只接受 --providers mysql。");
        }

        var artifactGroup = mode switch
        {
            AuditingQueryBenchmarkMode.SqlServerPlanAb => "auditing-query-sqlserver-ab",
            AuditingQueryBenchmarkMode.MySqlIndexAb => "auditing-query-mysql-index-ab",
            AuditingQueryBenchmarkMode.MySqlLateMaterializationAb =>
                "auditing-query-mysql-late-materialization-ab",
            AuditingQueryBenchmarkMode.CursorAb => "auditing-query-cursor-ab",
            _ => "auditing-query",
        };
        var outputDirectory = values.GetValueOrDefault(
            "--output",
            Path.Combine(
                "BenchmarkDotNet.Artifacts",
                artifactGroup,
                DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)));

        return new AuditingQueryBenchmarkOptions(
            rows,
            warmup,
            iterations,
            pageSize,
            Concurrency: 1,
            referenceUtc,
            outputDirectory,
            providers,
            mode);
    }

    private static Dictionary<string, string> ParsePairs(IReadOnlyList<string> arguments)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < arguments.Count; index += 2)
        {
            var key = arguments[index];
            if (!key.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"未知参数：{key}");
            }

            if (index + 1 >= arguments.Count)
            {
                throw new ArgumentException($"参数 {key} 缺少值。");
            }

            if (!KnownOptions.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"未知参数：{key}");
            }

            values[key] = arguments[index + 1];
        }

        return values;
    }

    private static int ParseInt(
        IReadOnlyDictionary<string, string> values,
        string key,
        int defaultValue,
        int minimum,
        int maximum = int.MaxValue)
    {
        if (!values.TryGetValue(key, out var raw))
        {
            return defaultValue;
        }

        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
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
            return new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero);
        }

        if (!DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var value))
        {
            throw new ArgumentException("--reference-utc 必须是有效的 UTC 时间。");
        }

        return value;
    }

    private static IReadOnlyList<string> ParseProviders(
        IReadOnlyDictionary<string, string> values)
    {
        var providers = values.GetValueOrDefault("--providers", "sqlserver,mysql")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(provider => provider.ToLowerInvariant())
            .ToArray();
        if (providers.Length == 0
            || providers.Any(provider => provider is not ("sqlserver" or "mysql")))
        {
            throw new ArgumentException(
                "--providers 只能包含 sqlserver 和 mysql。");
        }

        return providers;
    }

    private static AuditingQueryBenchmarkMode ParseMode(
        IReadOnlyDictionary<string, string> values)
    {
        var mode = values.GetValueOrDefault("--mode", "baseline");
        return mode switch
        {
            "baseline" => AuditingQueryBenchmarkMode.Baseline,
            "sqlserver-plan-ab" => AuditingQueryBenchmarkMode.SqlServerPlanAb,
            "mysql-index-ab" => AuditingQueryBenchmarkMode.MySqlIndexAb,
            "mysql-late-materialization-ab" =>
                AuditingQueryBenchmarkMode.MySqlLateMaterializationAb,
            "cursor-ab" => AuditingQueryBenchmarkMode.CursorAb,
            _ => throw new ArgumentException(
                "--mode 只能是 baseline、sqlserver-plan-ab、mysql-index-ab "
                + "mysql-late-materialization-ab 或 cursor-ab。"),
        };
    }

    private static readonly string[] KnownOptions =
    [
        "--rows",
        "--warmup",
        "--iterations",
        "--page-size",
        "--providers",
        "--mode",
        "--reference-utc",
        "--output",
    ];
}
