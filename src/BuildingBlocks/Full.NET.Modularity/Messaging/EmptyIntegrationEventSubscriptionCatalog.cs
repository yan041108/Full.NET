using Full.NET.Abstractions.Messaging;
using Full.NET.Messaging.Abstractions;

namespace Full.NET.Modularity.Messaging;

/// <summary>
/// 空订阅目录默认值；用于未加载 Messaging 模块的精简宿主（如 API、集成测试夹具、事务 Outbox 工具）。
/// </summary>
/// <remarks>
/// 为什么需要默认空实现：
/// 1) Modularity 核心 AddFullNetModularity 会无条件注册 IntegrationEventConsumerDispatcher，
///    该 Dispatcher 依赖 IIntegrationEventSubscriptionCatalog 解析路由；若 Messaging 模块未加载，
///    没有默认实现会导致 DI 容器 Build 时因缺少服务而抛异常（DI 图不闭合）。
/// 2) 该实现所有查询行为都抛异常，语义等同"目录不存在"；只有 GetAllSubscriptions 返回空集合，
///    以便启动守卫能优雅地判断"没有任何业务订阅"。
/// 3) Scoped 生命周期：与真实 IntegrationEventSubscriptionCatalog 保持一致，
///    替换时无需调整 DI 注册的 Lifetime。
/// </remarks>
internal sealed class EmptyIntegrationEventSubscriptionCatalog : IIntegrationEventSubscriptionCatalog
{
    public IIntegrationEventSubscription GetRequired(
        string consumerName,
        string eventType,
        int schemaVersion) =>
        throw new InvalidOperationException(
            "No integration event subscription catalog is registered. "
            + "Ensure the Messaging module is loaded when using integration event delivery features. "
            + $"Requested route: consumer='{consumerName}', event='{eventType}', schema={schemaVersion}.");

    public IIntegrationEventSubscription GetByHandlerTypeRequired(Type handlerType) =>
        throw new InvalidOperationException(
            "No integration event subscription catalog is registered. "
            + "Ensure the Messaging module is loaded when using integration event delivery features. "
            + $"Requested handler type: '{handlerType?.FullName}'.");

    public EventDeliveryOwner GetDeliveryOwner(string eventType, int schemaVersion) =>
        throw new InvalidOperationException(
            "No integration event topic catalog is registered. "
            + "Ensure the Messaging module is loaded when using integration event delivery features. "
            + $"Requested stream: event='{eventType}', schema={schemaVersion}.");

    public EventDeliveryOwner ResolveDeliveryOwner(
        string eventType,
        int schemaVersion,
        EventDeliveryOwner? persistedCurrentOwner) =>
        throw new InvalidOperationException(
            "No integration event topic catalog is registered. "
            + "Ensure the Messaging module is loaded when using integration event delivery features. "
            + $"Requested stream: event='{eventType}', schema={schemaVersion}.");

    public IntegrationEventTopicDefinition GetTopicRequired(
        string eventType,
        int schemaVersion) =>
        throw new InvalidOperationException(
            "No integration event topic catalog is registered. "
            + "Ensure the Messaging module is loaded when using integration event delivery features. "
            + $"Requested stream: event='{eventType}', schema={schemaVersion}.");

    public IntegrationEventTopicDefinition GetTopicByCodeRequired(string topicCode) =>
        throw new InvalidOperationException(
            "No integration event topic catalog is registered. "
            + "Ensure the Messaging module is loaded when using integration event delivery features. "
            + $"Requested topic: topicCode='{topicCode}'.");

    /// <summary>
    /// 返回空集合；启动守卫通过该方法判断 CdcKafka 模式下是否存在生产订阅。
    /// </summary>
    public IReadOnlyCollection<IIntegrationEventSubscription> GetAllSubscriptions() =>
        Array.Empty<IIntegrationEventSubscription>();
}
