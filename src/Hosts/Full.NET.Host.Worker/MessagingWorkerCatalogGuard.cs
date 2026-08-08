using Full.NET.Messaging.Abstractions;

namespace Full.NET.Host.Worker;

/// <summary>
/// 在 Worker 启动时校验消息模式与 Topic/订阅目录是否冲突。
/// </summary>
internal static class MessagingWorkerCatalogGuard
{
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
