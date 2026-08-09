using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Host.Worker;

/// <summary>
/// 在 Worker 启动时校验消息模式与 Topic/订阅目录是否冲突。
/// </summary>
/// <remarks>
/// 不变量：所有与 Scoped Catalog/订阅 交互的入口必须通过 IServiceScopeFactory 创建作用域，
/// 不得在构造函数或根 Provider 中直接解析 Scoped 服务。
/// </remarks>
internal static class MessagingWorkerCatalogGuard
{
    /// <summary>
    /// 通过 IServiceScopeFactory 创建 AsyncScope 解析目录，校验 HybridKafka（含过时 CdcKafka 别名）模式。
    /// </summary>
    /// <param name="scopeFactory">DI 作用域工厂；通常由 Singleton 启动守卫或 HostedService 持有。</param>
    /// <param name="mode">当前 Worker 运行模式。</param>
    /// <remarks>
    /// 为什么必须走 ScopeFactory：
    /// 1) IIntegrationEventSubscriptionCatalog 是 Scoped，根 Provider（含启动阶段的 IStartupValidator）
    ///    直接解析会触发生命周期不匹配异常；
    /// 2) 空目录默认值实现（EmptyIntegrationEventSubscriptionCatalog）返回空集合，
    ///    通过 catalog.GetAllSubscriptions() 判断比直接 GetServices 更可靠——目录构造已完成一致性校验。
    /// </remarks>
    public static async Task ValidateCdcKafkaModeAsync(
        IServiceScopeFactory scopeFactory,
        MessagingWorkerMode mode)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        // CdcKafka 枚举值作为 HybridKafka 的过时别名，两者共享同一守卫。
#pragma warning disable CS0618 // CdcKafka 作为过时别名保留一版，比较操作是兼容旧配置的必要步骤。
        var effectiveMode = mode == MessagingWorkerMode.CdcKafka
            ? MessagingWorkerMode.HybridKafka
            : mode;
#pragma warning restore CS0618

        if (effectiveMode is not MessagingWorkerMode.HybridKafka)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var catalog = provider
            .GetRequiredService<IIntegrationEventSubscriptionCatalog>();
        var subscriptions = catalog.GetAllSubscriptions();
        var topics = provider
            .GetServices<IntegrationEventTopicDefinition>()
            .ToArray();

        // 使用按流守卫：逐个检查 owner=CdcKafka 的 Topic 是否存在至少一个匹配订阅。
        ValidateHybridKafkaMode(subscriptions, topics);
    }

    /// <summary>
    /// 正式 Kafka 模式必须存在真实业务订阅；空目录会让 Worker 看似健康却静默退出。
    /// </summary>
    /// <remarks>保留直接传集合的重载，便于单元测试（不依赖 DI 容器）。</remarks>
    public static void ValidateCdcKafkaMode(
        IReadOnlyCollection<IIntegrationEventSubscription> subscriptions)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        if (subscriptions.Count == 0)
        {
            throw new InvalidOperationException(
                "CdcKafka delivery mode requires at least one production IIntegrationEventSubscription; "
                + "currently no module registered any. See docs/operations/cdc-kafka-event-delivery.md.");
        }
    }

    /// <summary>
    /// HybridKafka 模式按流校验订阅：每个所有权为 <see cref="EventDeliveryOwner.CdcKafka"/> 的 Topic
    /// 必须存在至少一个匹配 (EventType, SchemaVersion) 的订阅；默认全 Legacy 流允许零订阅启动。
    /// </summary>
    /// <param name="subscriptions">已注册的生产订阅集合。</param>
    /// <param name="topics">Topic 定义目录（包含每个流的所有权）。</param>
    /// <exception cref="InvalidOperationException">任一 CdcKafka 流缺少匹配订阅时抛出。</exception>
    public static void ValidateHybridKafkaMode(
        IReadOnlyCollection<IIntegrationEventSubscription> subscriptions,
        IReadOnlyCollection<IntegrationEventTopicDefinition> topics)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        ArgumentNullException.ThrowIfNull(topics);

        var catalog = new IntegrationEventSubscriptionCatalog(topics, subscriptions);

        foreach (var topic in topics)
        {
            var owner = catalog.GetDeliveryOwner(topic.EventType, topic.SchemaVersion);
            if (owner != EventDeliveryOwner.CdcKafka)
            {
                continue;
            }

            // 按 (EventType, SchemaVersion) 精确匹配订阅，不允许跨 schema 或跨类型复用。
            var hasMatchingSubscription = subscriptions.Any(subscription =>
                string.Equals(
                    subscription.EventType,
                    topic.EventType,
                    StringComparison.Ordinal)
                && subscription.SchemaVersion == topic.SchemaVersion);

            if (!hasMatchingSubscription)
            {
                throw new InvalidOperationException(
                    $"HybridKafka delivery mode requires at least one IIntegrationEventSubscription "
                    + $"for CdcKafka-owned event stream '{topic.EventType}' schema {topic.SchemaVersion}; "
                    + $"currently no module registered a matching subscription. "
                    + $"See docs/operations/cdc-kafka-event-delivery.md.");
            }
        }
    }

    /// <summary>
    /// Shadow 模式禁止为 <see cref="EventDeliveryOwner.CdcKafka"/> 事件流注册业务订阅。
    /// </summary>
    public static void ValidateShadowMode(
        IReadOnlyCollection<IIntegrationEventSubscription> subscriptions,
        IReadOnlyCollection<IntegrationEventTopicDefinition> topics)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        ArgumentNullException.ThrowIfNull(topics);

        if (subscriptions.Count == 0)
        {
            return;
        }

        var catalog = new IntegrationEventSubscriptionCatalog(topics, subscriptions);
        foreach (var subscription in subscriptions)
        {
            var owner = catalog.GetDeliveryOwner(
                subscription.EventType,
                subscription.SchemaVersion);
            if (owner == EventDeliveryOwner.CdcKafka)
            {
                throw new InvalidOperationException(
                    "Messaging:Worker mode ShadowCdc cannot register business subscriptions "
                    + $"for CdcKafka-owned stream '{subscription.EventType}' "
                    + $"schema {subscription.SchemaVersion}.");
            }
        }
    }
}
