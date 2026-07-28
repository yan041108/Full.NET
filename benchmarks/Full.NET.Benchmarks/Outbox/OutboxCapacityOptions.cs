using System.Globalization;

namespace Full.NET.Benchmarks.Outbox;

public sealed record OutboxCapacityOptions(
    IReadOnlyList<string> Providers,
    IReadOnlyList<int> ConcurrencyLevels,
    IReadOnlyList<int> HandlerDelayMilliseconds,
    IReadOnlyList<int> ReplicaCounts,
    IReadOnlyList<int> BatchSizes,
    IReadOnlyList<int> PayloadSizes,
    int Repetitions,
    TimeSpan Warmup,
    TimeSpan Duration,
    int SeedMessages,
    TimeSpan Lease,
    TimeSpan LeaseRenewal,
    string OutputDirectory)
{
    public const string HelpText =
        """
        Full.NET Outbox 消费容量矩阵

        用法：
          dotnet run --project benchmarks/Full.NET.Benchmarks -c Release -- outbox-capacity [选项]

        选项：
          --providers <list>              sqlserver,mysql 的逗号分隔子集，默认两者
          --concurrency <list>            单副本消息并发，默认 1,2,4,8
          --handler-delay-ms <list>       Handler 延迟毫秒，默认 0,10,100,1000
          --replicas <list>               Worker 副本数，默认 1,2
          --batch-sizes <list>            领取批量，默认 20,100
          --payload-sizes <list>          Payload 字节数，默认 256,4096
          --repetitions <n>               每个场景重复次数，默认 3
          --warmup-seconds <n>            每个场景预热秒数，默认 10
          --duration-seconds <n>          每个场景采样秒数，默认 30
          --seed-messages <n>             每个场景预置消息数，默认 20000
          --lease-seconds <n>             租约秒数，默认 30
          --lease-renewal-seconds <n>     续租周期秒数，默认 10，不得超过租约一半
          --output <path>                 工件目录，默认 BenchmarkDotNet.Artifacts/outbox-capacity/<UTC>
          --help                          显示帮助

        正式矩阵会遍历并发、延迟和副本数；批量与 Payload 只在参考并发档做组合，
        避免把一次容量验证放大为数小时的无界笛卡尔积。开发期请缩小列表并设 repetitions=1。
        """;

    public static OutboxCapacityOptions Parse(IReadOnlyList<string> arguments)
    {
        var values = ParsePairs(arguments);
        var providers = ParseProviders(values);
        var concurrency = ParseList(
            values,
            "--concurrency",
            "1,2,4,8",
            minimum: 1,
            maximum: 16);
        var handlerDelays = ParseList(
            values,
            "--handler-delay-ms",
            "0,10,100,1000",
            minimum: 0,
            maximum: 60_000);
        var replicas = ParseList(
            values,
            "--replicas",
            "1,2",
            minimum: 1,
            maximum: 16);
        var batchSizes = ParseList(
            values,
            "--batch-sizes",
            "20,100",
            minimum: 1,
            maximum: 200);
        var payloadSizes = ParseList(
            values,
            "--payload-sizes",
            "256,4096",
            minimum: 64,
            maximum: 1_048_576);
        if (concurrency.Max() > batchSizes.Min())
        {
            throw new ArgumentException(
                "--concurrency 的最大值不得超过 --batch-sizes 的最小值。");
        }
        var repetitions = ParseBoundedInt(
            values,
            "--repetitions",
            defaultValue: 3,
            minimum: 1,
            maximum: 20);
        var warmupSeconds = ParseBoundedInt(
            values,
            "--warmup-seconds",
            defaultValue: 10,
            minimum: 0,
            maximum: 3600);
        var durationSeconds = ParseBoundedInt(
            values,
            "--duration-seconds",
            defaultValue: 30,
            minimum: 1,
            maximum: 86_400);
        var seedMessages = ParseBoundedInt(
            values,
            "--seed-messages",
            defaultValue: 20_000,
            minimum: 1,
            maximum: 10_000_000);
        var leaseSeconds = ParseBoundedInt(
            values,
            "--lease-seconds",
            defaultValue: 30,
            minimum: 5,
            maximum: 3600);
        var leaseRenewalSeconds = ParseBoundedInt(
            values,
            "--lease-renewal-seconds",
            defaultValue: 10,
            minimum: 1,
            maximum: 3599);
        if (leaseRenewalSeconds > leaseSeconds / 2)
        {
            throw new ArgumentException(
                "--lease-renewal-seconds 不得超过 --lease-seconds 的一半。");
        }

        var outputDirectory = values.GetValueOrDefault(
            "--output",
            Path.Combine(
                "BenchmarkDotNet.Artifacts",
                "outbox-capacity",
                DateTimeOffset.UtcNow.ToString(
                    "yyyyMMdd-HHmmss",
                    CultureInfo.InvariantCulture)));
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("--output 不能为空。", "--output");
        }

        return new OutboxCapacityOptions(
            providers,
            concurrency,
            handlerDelays,
            replicas,
            batchSizes,
            payloadSizes,
            repetitions,
            TimeSpan.FromSeconds(warmupSeconds),
            TimeSpan.FromSeconds(durationSeconds),
            seedMessages,
            TimeSpan.FromSeconds(leaseSeconds),
            TimeSpan.FromSeconds(leaseRenewalSeconds),
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
        "--handler-delay-ms",
        "--replicas",
        "--batch-sizes",
        "--payload-sizes",
        "--repetitions",
        "--warmup-seconds",
        "--duration-seconds",
        "--seed-messages",
        "--lease-seconds",
        "--lease-renewal-seconds",
        "--output",
    ];
}
