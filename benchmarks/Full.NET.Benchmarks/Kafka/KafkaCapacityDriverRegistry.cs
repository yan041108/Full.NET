using Full.NET.Messaging.Kafka;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 创建单一容量范围的执行 Driver 及其可选统计源。
/// </summary>
public interface IKafkaCapacityScenarioDriverFactory
{
    /// <summary>
    /// 获取进入预算、续跑和报告契约的稳定范围代码。
    /// </summary>
    string ScopeCode { get; }

    /// <summary>
    /// 使用统一加载的 Runner 配置创建 Driver；实现不得连接外部依赖或产生副作用。
    /// </summary>
    KafkaCapacityDriverRuntime Create(KafkaCapacityConfiguration configuration);
}

/// <summary>
/// 在任何 Kafka 管理操作前验证特定 Scope 的外部依赖与安全边界。
/// </summary>
public interface IKafkaCapacityDriverPreflight
{
    /// <summary>
    /// 执行无架构变更的前置检查；失败必须关闭本次容量执行。
    /// </summary>
    Task ValidateAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 保存一次容量执行共享的 Driver 和统计投影边界。
/// </summary>
public sealed record KafkaCapacityDriverRuntime(
    IKafkaCapacityScenarioDriver Driver,
    IKafkaCapacityStatisticsSource? StatisticsSource,
    IKafkaCapacityDriverPreflight? Preflight = null);

/// <summary>
/// 按稳定 ScopeCode 选择唯一 Driver Factory，防止未来范围复制公共控制面。
/// </summary>
public sealed class KafkaCapacityDriverRegistry
{
    private readonly IReadOnlyDictionary<string, IKafkaCapacityScenarioDriverFactory>
        factories;

    /// <summary>
    /// 构建注册表；同一范围存在多个 Factory 时失败关闭。
    /// </summary>
    public KafkaCapacityDriverRegistry(
        IEnumerable<IKafkaCapacityScenarioDriverFactory> factories)
    {
        ArgumentNullException.ThrowIfNull(factories);
        var resolved = new Dictionary<
            string,
            IKafkaCapacityScenarioDriverFactory>(StringComparer.Ordinal);
        foreach (var factory in factories)
        {
            ArgumentNullException.ThrowIfNull(factory);
            KafkaCapacityScopeCodes.Validate(factory.ScopeCode);
            if (!resolved.TryAdd(factory.ScopeCode, factory))
            {
                throw new InvalidDataException(
                    "Kafka capacity driver scope is registered more than once.");
            }
        }

        this.factories = resolved;
    }

    /// <summary>
    /// 获取精确范围的 Factory；未知范围不得回退到默认 Driver。
    /// </summary>
    public IKafkaCapacityScenarioDriverFactory GetRequired(string scopeCode)
    {
        KafkaCapacityScopeCodes.Validate(scopeCode);
        if (!factories.TryGetValue(scopeCode, out var factory))
        {
            throw new InvalidDataException(
                "Kafka capacity driver scope is not registered.");
        }

        return factory;
    }

    /// <summary>
    /// 创建并校验 Driver 运行时，禁止 Factory 与 Driver 范围漂移。
    /// </summary>
    public static KafkaCapacityDriverRuntime CreateRuntime(
        IKafkaCapacityScenarioDriverFactory factory,
        KafkaCapacityConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(configuration);
        var runtime = factory.Create(configuration)
            ?? throw new InvalidDataException(
                "Kafka capacity driver factory returned no runtime.");
        if (runtime.Driver is null
            || !string.Equals(
                runtime.Driver.ScopeCode,
                factory.ScopeCode,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Kafka capacity driver runtime scope does not match its factory.");
        }

        return runtime;
    }

    /// <summary>
    /// 创建包含已交付 Scope A/B/C Driver 的默认注册表。
    /// </summary>
    public static KafkaCapacityDriverRegistry CreateDefault() =>
        new([
            new KafkaTransportScenarioDriverFactory(),
            new KafkaWorkerScenarioDriverFactory(),
            new KafkaOutboxCdcScenarioDriverFactory(),
        ]);

    /// <summary>
    /// 判断 Runner 是否应创建并删除临时 owned Topic。
    /// </summary>
    public static bool UsesRunnerOwnedTopic(string scopeCode) =>
        !string.Equals(
            scopeCode,
            KafkaCapacityScopeCodes.TransactionOutboxCdc,
            StringComparison.Ordinal);
}

/// <summary>
/// 创建 Scope A 独立 Producer、Topic 和 Consumer 传输 Driver。
/// </summary>
public sealed class KafkaTransportScenarioDriverFactory
    : IKafkaCapacityScenarioDriverFactory
{
    /// <inheritdoc />
    public string ScopeCode => KafkaCapacityScopeCodes.KafkaTransport;

    /// <inheritdoc />
    public KafkaCapacityDriverRuntime Create(
        KafkaCapacityConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var executor = new KafkaCapacityTransportExecutor(
            configuration.Kafka,
            new ConfluentKafkaCapacityProducerFactory(),
            new ConfluentKafkaCapacityConsumerFactory());
        return new KafkaCapacityDriverRuntime(
            new KafkaTransportScenarioDriver(executor),
            executor);
    }
}
