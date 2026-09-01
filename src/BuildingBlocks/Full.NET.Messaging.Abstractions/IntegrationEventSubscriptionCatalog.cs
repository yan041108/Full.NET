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
    /// <summary>
    /// 按路由键解析唯一订阅；未注册时抛出异常。
    /// </summary>
    /// <param name="consumerName">订阅所属 Consumer Group 的稳定机器码。</param>
    /// <param name="eventType">集成事件类型的小写点分稳定机器码。</param>
    /// <param name="schemaVersion">事件载荷 Schema 版本号（从 1 开始）。</param>
    /// <returns>解析到的唯一订阅实例。</returns>
    /// <exception cref="ArgumentException"><paramref name="consumerName"/> 或 <paramref name="eventType"/> 不符合稳定机器码约束。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="schemaVersion"/> 小于 1。</exception>
    /// <exception cref="InvalidOperationException">路由在目录中未注册。</exception>
    IIntegrationEventSubscription GetRequired(
        string consumerName,
        string eventType,
        int schemaVersion);

    /// <summary>
    /// 按生成注册表声明的具体订阅类型解析当前 Scope 中的实例。
    /// </summary>
    /// <param name="handlerType">订阅实现类型，需与生成器注册表声明的类型完全一致。</param>
    /// <returns>解析到的唯一订阅实例。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handlerType"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvalidOperationException">该类型未在目录中注册为唯一订阅。</exception>
    IIntegrationEventSubscription GetByHandlerTypeRequired(Type handlerType);

    /// <summary>查询事件流在目录中声明的发布所有权。</summary>
    /// <param name="eventType">集成事件类型的小写点分稳定机器码。</param>
    /// <param name="schemaVersion">事件载荷 Schema 版本号（从 1 开始）。</param>
    /// <returns>该事件流目录声明的默认发布所有权。</returns>
    /// <exception cref="ArgumentException"><paramref name="eventType"/> 不符合稳定机器码约束。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="schemaVersion"/> 小于 1。</exception>
    EventDeliveryOwner GetDeliveryOwner(string eventType, int schemaVersion);

    /// <summary>在目录默认所有权之上叠加持久化切流记录，得到运行时有效所有权。</summary>
    /// <param name="eventType">集成事件类型的小写点分稳定机器码。</param>
    /// <param name="schemaVersion">事件载荷 Schema 版本号（从 1 开始）。</param>
    /// <param name="persistedCurrentOwner">持久化存储中的当前切流所有权；<see langword="null"/> 表示未切流。</param>
    /// <returns>运行时实际生效的发布所有权。</returns>
    EventDeliveryOwner ResolveDeliveryOwner(
        string eventType,
        int schemaVersion,
        EventDeliveryOwner? persistedCurrentOwner);

    /// <summary>查询事件流绑定的 Topic 目录条目。</summary>
    /// <param name="eventType">集成事件类型的小写点分稳定机器码。</param>
    /// <param name="schemaVersion">事件载荷 Schema 版本号（从 1 开始）。</param>
    /// <returns>绑定的 Topic 目录条目。</returns>
    /// <exception cref="ArgumentException"><paramref name="eventType"/> 不符合稳定机器码约束。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="schemaVersion"/> 小于 1。</exception>
    /// <exception cref="InvalidOperationException">该事件流未在 Topic 目录中注册。</exception>
    IntegrationEventTopicDefinition GetTopicRequired(
        string eventType,
        int schemaVersion);

    /// <summary>按稳定 TopicCode 查询目录条目；范围重放禁止使用目录外 Topic。</summary>
    /// <param name="topicCode">逻辑 Topic 的稳定机器码，需符合 <c>MessagingNames.TopicCodePattern</c>。</param>
    /// <returns>匹配的 Topic 目录条目。</returns>
    /// <exception cref="ArgumentException"><paramref name="topicCode"/> 为空或不符合稳定机器码约束。</exception>
    /// <exception cref="InvalidOperationException">该 TopicCode 未在目录中注册。</exception>
    IntegrationEventTopicDefinition GetTopicByCodeRequired(string topicCode);

    /// <summary>
    /// 返回目录中所有已注册的业务订阅；用于启动守卫检查 CdcKafka 模式是否存在真实生产订阅。
    /// </summary>
    /// <returns>目录构造阶段通过校验的全部订阅快照集合；只读，不可修改。</returns>
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
    private readonly IReadOnlyDictionary<Type, IIntegrationEventSubscription> _subscriptionsByHandlerType;
    private readonly IReadOnlyCollection<IIntegrationEventSubscription> _allSubscriptions;

    /// <summary>
    /// 从 Topic 与订阅集合构造目录，自动派生 Consumer 定义。
    /// </summary>
    /// <param name="topics">所有已声明的 Topic 定义集合。</param>
    /// <param name="subscriptions">所有已声明的业务订阅集合。</param>
    /// <remarks>
    /// 通过 <paramref name="subscriptions"/> 中的 <c>ConsumerName</c> 自动去重派生 <see cref="IntegrationEventConsumerDefinition"/>，
    /// 适用于调用方不关心消费者目录的场景。
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="topics"/> 或 <paramref name="subscriptions"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvalidOperationException">存在重复 TopicCode、重复消费者名、重复路由键或引用未注册 Topic。</exception>
    public IntegrationEventSubscriptionCatalog(
        IEnumerable<IntegrationEventTopicDefinition> topics,
        IEnumerable<IIntegrationEventSubscription> subscriptions)
        : this(topics, DeriveConsumers(subscriptions), subscriptions)
    {
    }

    /// <summary>
    /// 从 Topic、Consumer 与订阅集合构造目录，并在构造期校验所有不变量。
    /// </summary>
    /// <param name="topics">所有已声明的 Topic 定义集合。</param>
    /// <param name="consumers">所有已声明的 Kafka Consumer Group 稳定身份。</param>
    /// <param name="subscriptions">所有已声明的业务订阅集合。</param>
    /// <remarks>
    /// 构造阶段会同步校验：TopicCode 唯一、ConsumerName 唯一且合规、路由三元组唯一、
    /// 订阅引用的 Topic 存在、幂等策略可识别；任一校验失败立即抛出 <see cref="InvalidOperationException"/>，
    /// 避免运行期才发现目录错乱。
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="topics"/>、<paramref name="consumers"/> 或 <paramref name="subscriptions"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvalidOperationException">存在重复 TopicCode、重复消费者名、重复路由键或引用未注册 Topic。</exception>
    public IntegrationEventSubscriptionCatalog(
        IEnumerable<IntegrationEventTopicDefinition> topics,
        IEnumerable<IntegrationEventConsumerDefinition> consumers,
        IEnumerable<IIntegrationEventSubscription> subscriptions)
    {
        ArgumentNullException.ThrowIfNull(topics);
        ArgumentNullException.ThrowIfNull(consumers);
        ArgumentNullException.ThrowIfNull(subscriptions);

        var subscriptionSnapshot = subscriptions.ToArray();
        _topicsByCode = BuildTopicsByCode(topics);
        _topicsByEvent = BuildTopicsByEvent(topics);
        var registeredConsumers = RegisterConsumers(consumers);
        _subscriptionsByRoute = RegisterSubscriptions(
            subscriptionSnapshot,
            _topicsByEvent,
            registeredConsumers);
        _subscriptionsByHandlerType = RegisterHandlerTypes(subscriptionSnapshot);
        // 保留一份订阅快照用于启动守卫查询；ToArray 保证与注册验证过程一致且不可变。
        _allSubscriptions = subscriptionSnapshot;
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
    /// 按生成注册表声明的具体订阅类型解析当前 Scope 中的实例。
    /// </summary>
    /// <param name="handlerType">订阅实现类型，需与生成器注册表声明的类型完全一致。</param>
    /// <returns>解析到的唯一订阅实例。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handlerType"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvalidOperationException">该类型未在目录中注册为唯一订阅。</exception>
    public IIntegrationEventSubscription GetByHandlerTypeRequired(Type handlerType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        if (_subscriptionsByHandlerType.TryGetValue(handlerType, out var subscription))
        {
            return subscription;
        }

        throw new InvalidOperationException(
            $"Integration event subscription handler type '{handlerType.FullName}' is not registered in the catalog.");
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
    /// 按稳定 TopicCode 查询目录条目，供运维工具在访问 Broker 前完成白名单校验。
    /// </summary>
    public IntegrationEventTopicDefinition GetTopicByCodeRequired(string topicCode)
    {
        if (string.IsNullOrWhiteSpace(topicCode)
            || !MessagingNames.TopicCodePattern.IsMatch(topicCode))
        {
            throw new ArgumentException(
                IntegrationEventFailureCodes.TopicCodeInvalid,
                nameof(topicCode));
        }

        if (_topicsByCode.TryGetValue(topicCode, out var topic))
        {
            return topic;
        }

        throw new InvalidOperationException(
            $"Topic code '{topicCode}' is not registered in the integration event catalog.");
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

    private static IReadOnlyDictionary<Type, IIntegrationEventSubscription> RegisterHandlerTypes(
        IEnumerable<IIntegrationEventSubscription> subscriptions) =>
        subscriptions
            .GroupBy(subscription => subscription.GetType())
            // 兼容旧轮询 Adapter 一种类型承载多条路由；生成注册表只会指向唯一具体类型。
            .Where(group => group.Take(2).Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single());

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
