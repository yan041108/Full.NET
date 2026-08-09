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
        return catalog.ResolveDeliveryOwner(eventType, schemaVersion, persisted?.CurrentOwner);
    }
}
