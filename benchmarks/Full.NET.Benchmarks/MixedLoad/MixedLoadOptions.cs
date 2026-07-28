using System.Globalization;

namespace Full.NET.Benchmarks.MixedLoad;

public sealed record MixedLoadOptions(
    IReadOnlyList<string> Providers,
    IReadOnlyList<int> ConcurrencyLevels,
    TimeSpan Warmup,
    TimeSpan Duration,
    int Seed,
    double MaximumUnexpectedErrorRate,
    string OutputDirectory,
    MixedLoadWorkload Workload,
    IReadOnlyList<MixedLoadAuditWriteProfile> AuditWriteProfiles,
    IReadOnlyList<MixedLoadOutboxRetentionProfile> OutboxRetentionProfiles,
    int OutboxRetentionSeedProcessed,
    int OutboxRetentionBatchSize,
    int OutboxRetentionMaxBatches,
    TimeSpan OutboxRetentionInterval)
{
    public bool OutboxRetentionMatrixEnabled =>
        OutboxRetentionProfiles.Count > 0;

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
          --workload <name>        default 或 audit-write，默认 default
          --audit-write-profiles   归因组合列表：none,access,operation,exception,all
          --outbox-retention-profiles
                                   Outbox 清理 A/B：off,on；默认不启用
          --outbox-retention-seed-processed <n>
                                   每档预置的过期成功消息数，默认 10000
          --outbox-retention-batch-size <n>
                                   清理单批上限 1..1000，默认 200
          --outbox-retention-max-batches <n>
                                   单轮清理批次数 1..100，默认 15
          --outbox-retention-interval-ms <n>
                                   清理批间隔 0..60000ms，默认 0
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

        var workload = ParseWorkload(values);
        var auditWriteProfiles = ParseAuditWriteProfiles(values, workload);
        var outboxRetentionProfiles = ParseOutboxRetentionProfiles(
            values,
            workload);
        var outboxRetentionSeedProcessed = ParseBoundedInt(
            values,
            "--outbox-retention-seed-processed",
            defaultValue: 10_000,
            minimum: 1,
            maximum: 1_000_000);
        var outboxRetentionBatchSize = ParseBoundedInt(
            values,
            "--outbox-retention-batch-size",
            defaultValue: 200,
            minimum: 1,
            maximum: 1000);
        var outboxRetentionMaxBatches = ParseBoundedInt(
            values,
            "--outbox-retention-max-batches",
            defaultValue: 15,
            minimum: 1,
            maximum: 100);
        var outboxRetentionIntervalMilliseconds = ParseBoundedInt(
            values,
            "--outbox-retention-interval-ms",
            defaultValue: 0,
            minimum: 0,
            maximum: 60_000);
        var hasRetentionTuning = values.Keys.Any(key =>
            key.StartsWith(
                "--outbox-retention-",
                StringComparison.OrdinalIgnoreCase)
            && !key.Equals(
                "--outbox-retention-profiles",
                StringComparison.OrdinalIgnoreCase));
        if (hasRetentionTuning && outboxRetentionProfiles.Count == 0)
        {
            throw new ArgumentException(
                "Outbox retention 调优参数必须与 --outbox-retention-profiles 一起使用。");
        }

        return new MixedLoadOptions(
            providers,
            concurrency,
            TimeSpan.FromSeconds(warmupSeconds),
            TimeSpan.FromSeconds(durationSeconds),
            seed,
            maximumErrorRate,
            outputDirectory,
            workload,
            auditWriteProfiles,
            outboxRetentionProfiles,
            outboxRetentionSeedProcessed,
            outboxRetentionBatchSize,
            outboxRetentionMaxBatches,
            TimeSpan.FromMilliseconds(
                outboxRetentionIntervalMilliseconds));
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

    private static int ParseBoundedInt(
        IReadOnlyDictionary<string, string> values,
        string key,
        int defaultValue,
        int minimum,
        int maximum)
    {
        var value = ParseInt(values, key, defaultValue);
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                key,
                value,
                $"{key} 必须位于 {minimum}..{maximum}。");
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

    private static MixedLoadWorkload ParseWorkload(
        IReadOnlyDictionary<string, string> values) =>
        values.GetValueOrDefault("--workload", "default").ToLowerInvariant() switch
        {
            "default" => MixedLoadWorkload.Default,
            "audit-write" => MixedLoadWorkload.AuditWrite,
            var value => throw new ArgumentException(
                $"--workload 不支持 {value}。",
                "--workload"),
        };

    private static IReadOnlyList<MixedLoadAuditWriteProfile> ParseAuditWriteProfiles(
        IReadOnlyDictionary<string, string> values,
        MixedLoadWorkload workload)
    {
        var hasExplicitProfiles = values.TryGetValue(
            "--audit-write-profiles",
            out var raw);
        if (hasExplicitProfiles && workload != MixedLoadWorkload.AuditWrite)
        {
            throw new ArgumentException(
                "--audit-write-profiles 只能用于 audit-write workload。",
                "--audit-write-profiles");
        }

        var profiles = (raw ?? "all")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseAuditWriteProfile)
            .ToArray();
        if (profiles.Length == 0
            || profiles.Distinct().Count() != profiles.Length)
        {
            throw new ArgumentException(
                "--audit-write-profiles 不能为空或包含重复值。",
                "--audit-write-profiles");
        }

        return profiles;
    }

    private static MixedLoadAuditWriteProfile ParseAuditWriteProfile(string value) =>
        value.ToLowerInvariant() switch
        {
            "none" => MixedLoadAuditWriteProfile.None,
            "access" => MixedLoadAuditWriteProfile.Access,
            "operation" => MixedLoadAuditWriteProfile.Operation,
            "exception" => MixedLoadAuditWriteProfile.Exception,
            "all" => MixedLoadAuditWriteProfile.All,
            _ => throw new ArgumentException(
                $"不支持的 Audit 写入 profile：{value}",
                "--audit-write-profiles"),
        };

    private static IReadOnlyList<MixedLoadOutboxRetentionProfile>
        ParseOutboxRetentionProfiles(
            IReadOnlyDictionary<string, string> values,
            MixedLoadWorkload workload)
    {
        if (!values.TryGetValue("--outbox-retention-profiles", out var raw))
        {
            return [];
        }

        if (workload != MixedLoadWorkload.Default)
        {
            throw new ArgumentException(
                "--outbox-retention-profiles 只能用于 default workload。",
                "--outbox-retention-profiles");
        }

        var profiles = raw
            .Split(
                ',',
                StringSplitOptions.TrimEntries
                | StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.ToLowerInvariant() switch
            {
                "off" => MixedLoadOutboxRetentionProfile.Off,
                "on" => MixedLoadOutboxRetentionProfile.On,
                _ => throw new ArgumentException(
                    $"不支持的 Outbox retention profile：{value}",
                    "--outbox-retention-profiles"),
            })
            .ToArray();
        if (profiles.Length == 0
            || profiles.Distinct().Count() != profiles.Length)
        {
            throw new ArgumentException(
                "--outbox-retention-profiles 不能为空或包含重复值。",
                "--outbox-retention-profiles");
        }

        if (profiles.Length != 2
            || !profiles.Contains(MixedLoadOutboxRetentionProfile.Off)
            || !profiles.Contains(MixedLoadOutboxRetentionProfile.On))
        {
            throw new ArgumentException(
                "--outbox-retention-profiles 必须同时包含 off,on。",
                "--outbox-retention-profiles");
        }

        return profiles;
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
        "--workload",
        "--audit-write-profiles",
        "--outbox-retention-profiles",
        "--outbox-retention-seed-processed",
        "--outbox-retention-batch-size",
        "--outbox-retention-max-batches",
        "--outbox-retention-interval-ms",
    ];
}

public enum MixedLoadOutboxRetentionProfile
{
    Off = 0,
    On = 1,
}
