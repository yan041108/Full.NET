using Full.NET.Abstractions.Messaging;

namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 已注册的 Kafka Consumer Group 稳定身份。
/// </summary>
/// <param name="ConsumerName">Consumer Group 机器码，进入遥测 <c>consumer_code</c> 标签。</param>
public sealed record IntegrationEventConsumerDefinition(string ConsumerName);

/// <summary>
/// Topic 与业务订阅目录的查询入口；Scoped 生命周期保证与 Handler/Inbox 事务作用域一致。
/// </summary>
/// <remarks>
/// 为什么是接口 + Scoped：
/// 1) Singleton HostedService（如 KafkaConsumerWorker）不得直接持有 Scoped 依赖，必须通过 IServiceScopeFactory 解析；
/// 2) 精简宿主（如仅注册 Dispatcher 的测试/集成夹具）需要一个空目录默认值实现来闭合 DI 图；
/// 3) 不同模块装配阶段可以替换真实实现，而调用方不依赖具体构造方式。
/// </remarks>
public interface IIntegrationEventSubscriptionCatalog
{
    /// <summary>按路由键解析唯一订阅；未注册时抛出异常。</summary>
    IIntegrationEventSubscription GetRequired(
        string consumerName,
        string eventType,
        int schemaVersion);

    /// <summary>查询事件流在目录中声明的发布所有权。</summary>
    EventDeliveryOwner GetDeliveryOwner(string eventType, int schemaVersion);

    /// <summary>在目录默认所有权之上叠加持久化切流记录，得到运行时有效所有权。</summary>
    EventDeliveryOwner ResolveDeliveryOwner(
        string eventType,
        int schemaVersion,
        EventDeliveryOwner? persistedCurrentOwner);

    /// <summary>查询事件流绑定的 Topic 目录条目。</summary>
    IntegrationEventTopicDefinition GetTopicRequired(
        string eventType,
        int schemaVersion);

    /// <summary>
    /// 返回目录中所有已注册的业务订阅；用于启动守卫检查 CdcKafka 模式是否存在真实生产订阅。
    /// </summary>
    IReadOnlyCollection<IIntegrationEventSubscription> GetAllSubscriptions();
}

/// <summary>
/// 静态 Topic 与订阅目录；路由键为 (ConsumerName, EventType, SchemaVersion)。
/// Scoped 生命周期：目录内的 Handler 订阅由模块装配，必须与每次消费的事务/Inbox 作用域保持一致。
/// </summary>
public sealed class IntegrationEventSubscriptionCatalog : IIntegrationEventSubscriptionCatalog
{
    private readonly IReadOnlyDictionary<string, IntegrationEventTopicDefinition> _topicsByCode;
    private readonly IReadOnlyDictionary<(string EventType, int SchemaVersion), IntegrationEventTopicDefinition> _topicsByEvent;
    private readonly IReadOnlyDictionary<SubscriptionRoute, IIntegrationEventSubscription> _subscriptionsByRoute;
    private readonly IReadOnlyCollection<IIntegrationEventSubscription> _allSubscriptions;

    public IntegrationEventSubscriptionCatalog(
        IEnumerable<IntegrationEventTopicDefinition> topics,
        IEnumerable<IIntegrationEventSubscription> subscriptions)
        : this(topics, DeriveConsumers(subscriptions), subscriptions)
    {
    }

    public IntegrationEventSubscriptionCatalog(
        IEnumerable<IntegrationEventTopicDefinition> topics,
        IEnumerable<IntegrationEventConsumerDefinition> consumers,
        IEnumerable<IIntegrationEventSubscription> subscriptions)
    {
        ArgumentNullException.ThrowIfNull(topics);
        ArgumentNullException.ThrowIfNull(consumers);
        ArgumentNullException.ThrowIfNull(subscriptions);

        _topicsByCode = BuildTopicsByCode(topics);
        _topicsByEvent = BuildTopicsByEvent(topics);
        var registeredConsumers = RegisterConsumers(consumers);
        _subscriptionsByRoute = RegisterSubscriptions(
            subscriptions,
            _topicsByEvent,
            registeredConsumers);
        // 保留一份订阅快照用于启动守卫查询；ToArray 保证与注册验证过程一致且不可变。
        _allSubscriptions = subscriptions.ToArray();
    }

    /// <summary>
    /// 按路由键解析唯一订阅；未注册时抛出异常。
    /// </summary>
    public IIntegrationEventSubscription GetRequired(
        string consumerName,
        string eventType,
        int schemaVersion)
    {
        ValidateConsumerName(consumerName);
        IntegrationEventEnvelope.ValidateMessageType(eventType);
        IntegrationEventEnvelope.ValidateSchemaVersion(schemaVersion);

        var route = new SubscriptionRoute(consumerName, eventType, schemaVersion);
        if (_subscriptionsByRoute.TryGetValue(route, out var subscription))
        {
            return subscription;
        }

        throw new InvalidOperationException(
            $"Integration event subscription route '{consumerName}' / '{eventType}' "
            + $"schema {schemaVersion} is not registered in the catalog.");
    }

    /// <summary>
    /// 查询事件流在目录中声明的发布所有权。
    /// </summary>
    public EventDeliveryOwner GetDeliveryOwner(string eventType, int schemaVersion)
    {
        IntegrationEventEnvelope.ValidateMessageType(eventType);
        IntegrationEventEnvelope.ValidateSchemaVersion(schemaVersion);
        return GetTopicRequired(eventType, schemaVersion).DeliveryOwner;
    }

    /// <summary>
    /// 在目录默认所有权之上叠加持久化切流记录，得到运行时有效所有权。
    /// </summary>
    public EventDeliveryOwner ResolveDeliveryOwner(
        string eventType,
        int schemaVersion,
        EventDeliveryOwner? persistedCurrentOwner)
    {
        if (persistedCurrentOwner is EventDeliveryOwner owner)
        {
            return owner;
        }

        return GetDeliveryOwner(eventType, schemaVersion);
    }

    /// <summary>
    /// 查询事件流绑定的 Topic 目录条目。
    /// </summary>
    public IntegrationEventTopicDefinition GetTopicRequired(
        string eventType,
        int schemaVersion)
    {
        IntegrationEventEnvelope.ValidateMessageType(eventType);
        IntegrationEventEnvelope.ValidateSchemaVersion(schemaVersion);

        if (_topicsByEvent.TryGetValue((eventType, schemaVersion), out var topic))
        {
            return topic;
        }

        throw new InvalidOperationException(
            $"Integration event stream '{eventType}' schema {schemaVersion} "
            + "is not registered in the topic catalog.");
    }

    /// <summary>
    /// 返回目录中所有已注册的业务订阅集合；CdcKafka 启动守卫通过该方法判断是否存在真实生产订阅。
    /// </summary>
    /// <remarks>
    /// 为什么不在守卫中直接 GetServices：
    /// 1) 守卫必须走 Scoped 解析路径，不能在 Root Provider 直接解析 IEnumerable；
    /// 2) 目录构造阶段已经做了订阅/Topic 一致性校验，返回目录内的快照保证了校验结果与可见性一致。
    /// </remarks>
    public IReadOnlyCollection<IIntegrationEventSubscription> GetAllSubscriptions() =>
        _allSubscriptions;

    private static IEnumerable<IntegrationEventConsumerDefinition> DeriveConsumers(
        IEnumerable<IIntegrationEventSubscription> subscriptions) =>
        subscriptions
            .Select(subscription => subscription.ConsumerName)
            .Distinct(StringComparer.Ordinal)
            .Select(consumerName => new IntegrationEventConsumerDefinition(consumerName));

    private static IReadOnlyDictionary<string, IntegrationEventTopicDefinition> BuildTopicsByCode(
        IEnumerable<IntegrationEventTopicDefinition> topics)
    {
        var topicsByCode = new Dictionary<string, IntegrationEventTopicDefinition>(
            StringComparer.Ordinal);
        foreach (var topic in topics)
        {
            if (!topicsByCode.TryAdd(topic.TopicCode, topic))
            {
                throw new InvalidOperationException(
                    $"Integration event topic catalog has a duplicate TopicCode "
                    + $"'{topic.TopicCode}'.");
            }
        }

        return topicsByCode;
    }

    private static IReadOnlyDictionary<(string EventType, int SchemaVersion), IntegrationEventTopicDefinition>
        BuildTopicsByEvent(IEnumerable<IntegrationEventTopicDefinition> topics)
    {
        var topicsByEvent =
            new Dictionary<(string EventType, int SchemaVersion), IntegrationEventTopicDefinition>(
                EventStreamComparer.Ordinal);
        foreach (var topic in topics)
        {
            var stream = (topic.EventType, topic.SchemaVersion);
            if (topicsByEvent.TryGetValue(stream, out var existingTopic))
            {
                throw CreateConflictingOwnershipException(existingTopic, topic);
            }

            topicsByEvent[stream] = topic;
        }

        return topicsByEvent;
    }

    private static HashSet<string> RegisterConsumers(
        IEnumerable<IntegrationEventConsumerDefinition> consumers)
    {
        var registeredConsumers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var consumer in consumers)
        {
            ValidateConsumerName(consumer.ConsumerName);
            if (!registeredConsumers.Add(consumer.ConsumerName))
            {
                throw new InvalidOperationException(
                    $"Integration event subscription catalog has a duplicate ConsumerName "
                    + $"'{consumer.ConsumerName}'.");
            }
        }

        return registeredConsumers;
    }

    private static IReadOnlyDictionary<SubscriptionRoute, IIntegrationEventSubscription>
        RegisterSubscriptions(
            IEnumerable<IIntegrationEventSubscription> subscriptions,
            IReadOnlyDictionary<(string EventType, int SchemaVersion), IntegrationEventTopicDefinition> topicsByEvent,
            ISet<string> registeredConsumers)
    {
        var subscriptionsByRoute = new Dictionary<SubscriptionRoute, IIntegrationEventSubscription>(
            SubscriptionRouteComparer.Ordinal);
        foreach (var subscription in subscriptions)
        {
            ValidateSubscription(subscription, topicsByEvent, registeredConsumers);

            var route = new SubscriptionRoute(
                subscription.ConsumerName,
                subscription.EventType,
                subscription.SchemaVersion);
            if (subscriptionsByRoute.TryGetValue(route, out var existingSubscription))
            {
                var existingOwner = existingSubscription.GetType().FullName
                    ?? existingSubscription.GetType().Name;
                var owner = subscription.GetType().FullName ?? subscription.GetType().Name;
                throw new InvalidOperationException(
                    $"Integration event route '{route.EventType}' schema {route.SchemaVersion} "
                    + $"within consumer '{route.ConsumerName}' is registered by both "
                    + $"'{existingOwner}' and '{owner}'.");
            }

            subscriptionsByRoute[route] = subscription;
        }

        return subscriptionsByRoute;
    }

    private static void ValidateSubscription(
        IIntegrationEventSubscription subscription,
        IReadOnlyDictionary<(string EventType, int SchemaVersion), IntegrationEventTopicDefinition> topicsByEvent,
        ISet<string> registeredConsumers)
    {
        var owner = subscription.GetType().FullName ?? subscription.GetType().Name;
        ValidateConsumerName(subscription.ConsumerName);
        if (!registeredConsumers.Contains(subscription.ConsumerName))
        {
            throw new InvalidOperationException(
                $"Integration event subscription '{owner}' references unknown ConsumerName "
                + $"'{subscription.ConsumerName}'. Register the consumer before binding routes.");
        }

        if (string.IsNullOrWhiteSpace(subscription.EventType))
        {
            throw new InvalidOperationException(
                $"Integration event subscription '{owner}' must declare a non-empty "
                + $"{nameof(IIntegrationEventSubscription.EventType)}.");
        }

        IntegrationEventEnvelope.ValidateMessageType(subscription.EventType);
        var schemaVersion = subscription.SchemaVersion;
        if (schemaVersion < 1)
        {
            throw new InvalidOperationException(
                $"Integration event subscription '{owner}' must declare a positive "
                + $"{nameof(IIntegrationEventSubscription.SchemaVersion)}.");
        }

        if (!topicsByEvent.ContainsKey((subscription.EventType, schemaVersion)))
        {
            throw new InvalidOperationException(
                $"Integration event subscription '{owner}' references unknown schema "
                + $"'{subscription.EventType}' version {schemaVersion}.");
        }

        var idempotencyStrategy = subscription.IdempotencyStrategy;
        if (idempotencyStrategy is not (
            IntegrationEventIdempotencyStrategy.NaturallyIdempotent
            or IntegrationEventIdempotencyStrategy.MessageIdDeduplication))
        {
            throw new InvalidOperationException(
                $"Integration event subscription '{owner}' must declare a supported "
                + $"{nameof(IIntegrationEventSubscription.IdempotencyStrategy)}.");
        }
    }

    internal static void ValidateConsumerName(string consumerName)
    {
        if (string.IsNullOrWhiteSpace(consumerName)
            || consumerName.Length > MessagingNames.ConsumerNameMaxLength
            || !MessagingNames.ConsumerNamePattern.IsMatch(consumerName))
        {
            throw new ArgumentException(
                IntegrationEventFailureCodes.ConsumerNameInvalid,
                nameof(consumerName));
        }
    }

    private static InvalidOperationException CreateConflictingOwnershipException(
        IntegrationEventTopicDefinition existingTopic,
        IntegrationEventTopicDefinition conflictingTopic)
    {
        if (HasFormalPublisherConflict(existingTopic.DeliveryOwner, conflictingTopic.DeliveryOwner))
        {
            return new InvalidOperationException(
                $"Integration event stream '{existingTopic.EventType}' schema "
                + $"{existingTopic.SchemaVersion} cannot be owned by both "
                + $"{existingTopic.DeliveryOwner} and {conflictingTopic.DeliveryOwner}.");
        }

        return new InvalidOperationException(
            $"Integration event stream '{existingTopic.EventType}' schema "
            + $"{existingTopic.SchemaVersion} is already registered by topic "
            + $"'{existingTopic.TopicCode}'.");
    }

    private static bool HasFormalPublisherConflict(
        EventDeliveryOwner existingOwner,
        EventDeliveryOwner conflictingOwner) =>
        existingOwner != conflictingOwner
        && IsFormalPublisher(existingOwner)
        && IsFormalPublisher(conflictingOwner);

    private static bool IsFormalPublisher(EventDeliveryOwner owner) =>
        owner is EventDeliveryOwner.LegacyPolling or EventDeliveryOwner.CdcKafka;

    private readonly record struct SubscriptionRoute(
        string ConsumerName,
        string EventType,
        int SchemaVersion);

    private sealed class SubscriptionRouteComparer : IEqualityComparer<SubscriptionRoute>
    {
        public static SubscriptionRouteComparer Ordinal { get; } = new();

        public bool Equals(SubscriptionRoute x, SubscriptionRoute y) =>
            x.SchemaVersion == y.SchemaVersion
            && string.Equals(x.ConsumerName, y.ConsumerName, StringComparison.Ordinal)
            && string.Equals(x.EventType, y.EventType, StringComparison.Ordinal);

        public int GetHashCode(SubscriptionRoute obj) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.ConsumerName),
                StringComparer.Ordinal.GetHashCode(obj.EventType),
                obj.SchemaVersion);
    }

    private sealed class EventStreamComparer : IEqualityComparer<(string EventType, int SchemaVersion)>
    {
        public static EventStreamComparer Ordinal { get; } = new();

        public bool Equals((string EventType, int SchemaVersion) x, (string EventType, int SchemaVersion) y) =>
            x.SchemaVersion == y.SchemaVersion
            && string.Equals(x.EventType, y.EventType, StringComparison.Ordinal);

        public int GetHashCode((string EventType, int SchemaVersion) obj) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(obj.EventType), obj.SchemaVersion);
    }
}