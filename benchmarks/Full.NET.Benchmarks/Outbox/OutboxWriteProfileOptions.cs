using System.Globalization;

using System.Text.Json.Serialization;

namespace Full.NET.Benchmarks.Outbox;

public enum OutboxWriteProfileTarget
{
    LegacyInsert,
    AppendOnly,
}

public enum OutboxWriteProfileCommandPath
{
    [JsonStringEnumMemberName("registry")]
    Registry,

    [JsonStringEnumMemberName("typed")]
    Typed,
}

internal static class OutboxWriteProfileCommandPathExtensions
{
    public static string ToToken(this OutboxWriteProfileCommandPath path) =>
        path switch
        {
            OutboxWriteProfileCommandPath.Registry => "registry",
            OutboxWriteProfileCommandPath.Typed => "typed",
            _ => throw new ArgumentOutOfRangeException(
                nameof(path),
                path,
                "Unsupported Outbox write profile command path."),
        };
}

public sealed record OutboxWriteProfileOptions(
    IReadOnlyList<string> Providers,
    IReadOnlyList<int> ConcurrencyLevels,
    IReadOnlyList<OutboxWriteProfileTarget> Targets,
    IReadOnlyList<OutboxWriteProfileCommandPath> CommandPaths,
    int PayloadSizeBytes,
    int Repetitions,
    TimeSpan Warmup,
    TimeSpan Duration,
    string OutputDirectory)
{
    public const string HelpText =
        """
        Full.NET Outbox 写入 Profile（P4 证据收集）

        用法：
          dotnet run --project benchmarks/Full.NET.Benchmarks -c Release -- outbox-write-profile [选项]

        选项：
          --providers <list>           sqlserver,mysql 的逗号分隔子集，默认两者
          --concurrency <list>         并发写入数，默认 1,8,32
          --targets <list>             legacy,append 的逗号分隔子集，默认两者
          --command-paths <list>       registry,typed 的逗号分隔子集，默认 registry
          --payload-size <bytes>       Payload 字节数，默认 256
          --repetitions <n>            每个场景重复次数，默认 3
          --warmup-seconds <n>         预热秒数，默认 10
          --duration-seconds <n>       采样秒数，默认 30
          --output <path>              工件目录，默认 BenchmarkDotNet.Artifacts/outbox-write-profile/<UTC>
          --help                       显示帮助
        """;

    public static OutboxWriteProfileOptions Parse(IReadOnlyList<string> arguments)
    {
        var values = ParsePairs(arguments);
        if (values.ContainsKey("--help"))
        {
            throw new ArgumentException("help");
        }

        var providers = ParseProviders(values);
        var concurrency = ParseList(
            values,
            "--concurrency",
            "1,8,32",
            minimum: 1,
            maximum: 64);
        var targets = ParseTargets(values);
        var commandPaths = ParseCommandPaths(values);
        var payloadSize = ParseBoundedInt(
            values,
            "--payload-size",
            defaultValue: 256,
            minimum: 64,
            maximum: 1_048_576);
        var repetitions = ParseBoundedInt(
            values,
            "--repetitions",
            defaultValue: 3,
            minimum: 1,
            maximum: 10);
        var warmupSeconds = ParseBoundedInt(
            values,
            "--warmup-seconds",
            defaultValue: 10,
            minimum: 1,
            maximum: 600);
        var durationSeconds = ParseBoundedInt(
            values,
            "--duration-seconds",
            defaultValue: 30,
            minimum: 5,
            maximum: 3600);
        var outputDirectory = values.GetValueOrDefault(
            "--output",
            Path.Combine(
                "BenchmarkDotNet.Artifacts",
                "outbox-write-profile",
                DateTimeOffset.UtcNow.ToString(
                    "yyyyMMdd-HHmmss",
                    CultureInfo.InvariantCulture)));
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("--output 不能为空。", "--output");
        }

        return new OutboxWriteProfileOptions(
            providers,
            concurrency,
            targets,
            commandPaths,
            payloadSize,
            repetitions,
            TimeSpan.FromSeconds(warmupSeconds),
            TimeSpan.FromSeconds(durationSeconds),
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
                throw new ArgumentException($"参数 {key} 不能重复。");
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

    private static IReadOnlyList<OutboxWriteProfileTarget> ParseTargets(
        IReadOnlyDictionary<string, string> values)
    {
        var targets = values.GetValueOrDefault(
                "--targets",
                "legacy,append")
            .Split(
                ',',
                StringSplitOptions.TrimEntries
                | StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseTarget)
            .ToArray();
        if (targets.Length == 0
            || targets.Distinct().Count() != targets.Length)
        {
            throw new ArgumentException(
                "--targets 不能为空或包含重复值。",
                "--targets");
        }

        return targets;
    }

    private static OutboxWriteProfileTarget ParseTarget(string value) =>
        value.ToLowerInvariant() switch
        {
            "legacy" => OutboxWriteProfileTarget.LegacyInsert,
            "append" => OutboxWriteProfileTarget.AppendOnly,
            _ => throw new ArgumentOutOfRangeException(
                "--targets",
                value,
                "只支持 legacy 与 append。"),
        };

    private static IReadOnlyList<OutboxWriteProfileCommandPath> ParseCommandPaths(
        IReadOnlyDictionary<string, string> values)
    {
        var paths = values.GetValueOrDefault(
                "--command-paths",
                "registry")
            .Split(
                ',',
                StringSplitOptions.TrimEntries
                | StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseCommandPath)
            .ToArray();
        if (paths.Length == 0 || paths.Distinct().Count() != paths.Length)
        {
            throw new ArgumentException(
                "--command-paths 不能为空或包含重复值。",
                "--command-paths");
        }

        return paths;
    }

    private static OutboxWriteProfileCommandPath ParseCommandPath(string value) =>
        value.ToLowerInvariant() switch
        {
            "registry" => OutboxWriteProfileCommandPath.Registry,
            "typed" => OutboxWriteProfileCommandPath.Typed,
            _ => throw new ArgumentOutOfRangeException(
                "--command-paths",
                value,
                "只支持 registry 与 typed。"),
        };

    private static IReadOnlyList<int> ParseList(
        IReadOnlyDictionary<string, string> values,
        string key,
        string defaultValue,
        int minimum,
        int maximum)
    {
        var rawValues = values.GetValueOrDefault(key, defaultValue)
            .Split(
                ',',
                StringSplitOptions.TrimEntries
                | StringSplitOptions.RemoveEmptyEntries);
        if (rawValues.Length == 0)
        {
            throw new ArgumentException($"{key} 至少需要一个值。", key);
        }

        var result = new int[rawValues.Length];
        for (var index = 0; index < rawValues.Length; index++)
        {
            if (!int.TryParse(
                    rawValues[index],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var value)
                || value < minimum
                || value > maximum)
            {
                throw new ArgumentOutOfRangeException(
                    key,
                    rawValues[index],
                    $"{key} 的值必须位于 {minimum}..{maximum}。");
            }

            result[index] = value;
        }

        if (result.Distinct().Count() != result.Length)
        {
            throw new ArgumentException($"{key} 不能包含重复值。", key);
        }

        return result;
    }

    private static int ParseBoundedInt(
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
                out var value))
        {
            throw new ArgumentException($"{key} 必须是整数。", key);
        }

        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                key,
                value,
                $"{key} 必须位于 {minimum}..{maximum}。");
        }

        return value;
    }

    private static readonly string[] KnownOptions =
    [
        "--providers",
        "--concurrency",
        "--targets",
        "--command-paths",
        "--payload-size",
        "--repetitions",
        "--warmup-seconds",
        "--duration-seconds",
        "--output",
        "--help",
    ];
}
