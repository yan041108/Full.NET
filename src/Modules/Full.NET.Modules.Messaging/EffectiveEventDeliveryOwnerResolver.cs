using Full.NET.Messaging.Abstractions;

namespace Full.NET.Modules.Messaging;

internal sealed class EffectiveEventDeliveryOwnerResolver(
    IntegrationEventSubscriptionCatalog catalog,
    IEventStreamOwnershipStore ownershipStore) : IEffectiveEventDeliveryOwnerResolver
{
    public async Task<EventDeliveryOwner> GetDeliveryOwnerAsync(
        string eventType,
        int schemaVersion,
        CancellationToken cancellationToken = default)
    {
        var persisted = await ownershipStore
            .FindAsync(eventType, schemaVersion, cancellationToken)
            .ConfigureAwait(false);
        if (persisted is not null)
        {
            return catalog.ResolveDeliveryOwner(
                eventType,
                schemaVersion,
                persisted.CurrentOwner);
        }

        try
        {
            return catalog.GetDeliveryOwner(eventType, schemaVersion);
        }
        catch (InvalidOperationException)
        {
            // Topic 目录在迁移期只登记准备进入 Broker 的流；未登记的既有可靠事件
            // 必须继续走 Legacy Worker，不能在业务事务中因目录不完整而停止写 Outbox。
            return EventDeliveryOwner.LegacyPolling;
        }
    }
}
