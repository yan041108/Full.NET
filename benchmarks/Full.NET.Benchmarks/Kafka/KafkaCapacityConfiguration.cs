using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Messaging.Kafka;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 表示独立 Kafka 容量运行器的环境配置。
/// </summary>
public sealed class KafkaCapacityConfiguration
{
    private const string EnvironmentPrefix = "KafkaCapacity__";

    /// <summary>
    /// 获取或设置环境是否允许实际执行容量负载。
    /// </summary>
    public bool ExecutionEnabled { get; set; }

    /// <summary>
    /// 获取或设置容量环境名称；生产环境始终禁止执行。
    /// </summary>
    public string EnvironmentName { get; set; } = "Capacity";

    /// <summary>
    /// 获取或设置执行前必须匹配的 Kafka Cluster Id。
    /// </summary>
    public string? ExpectedClusterId { get; set; }

    /// <summary>
    /// 获取或设置复用生产客户端构建器的 Kafka 配置。
    /// </summary>
    public KafkaMessagingOptions Kafka { get; set; } = new();

    /// <summary>
    /// 从可选 JSON 文件加载配置，并以环境变量覆盖已知配置键。
    /// </summary>
    public static KafkaCapacityConfiguration Load(
        KafkaCapacityOptions options,
        Func<string, string?>? environmentReader = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        environmentReader ??= Environment.GetEnvironmentVariable;

        var configuration = string.IsNullOrWhiteSpace(options.SettingsPath)
            ? new KafkaCapacityConfiguration()
            : LoadFile(options.SettingsPath);

        ApplyEnvironmentOverride(
            configuration,
            nameof(ExecutionEnabled),
            environmentReader,
            static (target, value) => target.ExecutionEnabled = ParseBoolean(value));
        ApplyEnvironmentOverride(
            configuration,
            nameof(EnvironmentName),
            environmentReader,
            static (target, value) => target.EnvironmentName = value);
        ApplyEnvironmentOverride(
            configuration,
            nameof(ExpectedClusterId),
            environmentReader,
            static (target, value) => target.ExpectedClusterId = value);
        ApplyKafkaEnvironmentOverrides(configuration, environmentReader);

        return configuration;
    }

    /// <summary>
    /// 返回不包含 Broker 地址、用户名或 Secret 的安全摘要。
    /// </summary>
    public override string ToString() =>
        $"ExecutionEnabled={ExecutionEnabled}; EnvironmentName={EnvironmentName}; "
        + $"ExpectedClusterIdConfigured={!string.IsNullOrWhiteSpace(ExpectedClusterId)}; "
        + $"KafkaEnabled={Kafka.Enabled}; SecurityProtocol={Kafka.SecurityProtocol}; "
        + $"SaslMechanism={Kafka.SaslMechanism}; PartitionsConfig=external";

    private static KafkaCapacityConfiguration LoadFile(string settingsPath)
    {
        var fullPath = Path.GetFullPath(settingsPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "Kafka capacity settings file was not found.",
                fullPath);
        }

        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        serializerOptions.Converters.Add(new JsonStringEnumConverter());

        using var stream = File.OpenRead(fullPath);
        var root = JsonSerializer.Deserialize<KafkaCapacityConfigurationRoot>(
            stream,
            serializerOptions);
        return root?.KafkaCapacity
            ?? throw new InvalidDataException(
                "KafkaCapacity settings section is required.");
    }

    private static void ApplyEnvironmentOverride(
        KafkaCapacityConfiguration configuration,
        string propertyName,
        Func<string, string?> environmentReader,
        Action<KafkaCapacityConfiguration, string> apply)
    {
        var value = environmentReader(EnvironmentPrefix + propertyName);
        if (value is not null)
        {
            apply(configuration, value);
        }
    }

    private static void ApplyKafkaEnvironmentOverrides(
        KafkaCapacityConfiguration configuration,
        Func<string, string?> environmentReader)
    {
        foreach (var property in typeof(KafkaMessagingOptions).GetProperties(
                     BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanWrite)
            {
                continue;
            }

            var value = environmentReader(
                $"{EnvironmentPrefix}Kafka__{property.Name}");
            if (value is null)
            {
                continue;
            }

            property.SetValue(
                configuration.Kafka,
                ConvertEnvironmentValue(value, property.PropertyType));
        }
    }

    private static object? ConvertEnvironmentValue(string value, Type targetType)
    {
        if (targetType == typeof(string))
        {
            return value;
        }

        if (targetType == typeof(string[]))
        {
            return value.Split(
                ',',
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }

        if (targetType == typeof(bool))
        {
            return ParseBoolean(value);
        }

        if (targetType == typeof(int)
            && int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var integer))
        {
            return integer;
        }

        if (targetType.IsEnum
            && Enum.TryParse(targetType, value, ignoreCase: true, out var enumValue)
            && enumValue is not null
            && Enum.IsDefined(targetType, enumValue))
        {
            return enumValue;
        }

        throw new InvalidDataException(
            $"Environment value for type '{targetType.Name}' is invalid.");
    }

    private static bool ParseBoolean(string value) =>
        bool.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidDataException("Environment boolean value is invalid.");

    private sealed class KafkaCapacityConfigurationRoot
    {
        public KafkaCapacityConfiguration? KafkaCapacity { get; set; }
    }
}
