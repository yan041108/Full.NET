using Full.NET.Abstractions.Messaging;
using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.Caching.Hybrid;

namespace Full.NET.Modules.Tenancy;

internal sealed class TenantProvisionedCacheInvalidationHandler(
    IIntegrationEventSerializer serializer,
    HybridCache cache) : IIntegrationEventHandler
{
    private static readonly string[] LegacyEventTypesValue =
        ["fullnet.tenancy.tenant-provisioned"];

    public string EventType => "fullnet.tenancy.tenant.provisioned";

    public IReadOnlyList<string> LegacyEventTypes => LegacyEventTypesValue;

    public int SchemaVersion => 1;

    public async Task HandleAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var integrationEvent = serializer
            .Deserialize<TenantProvisionedIntegrationEvent>(payload);
        await cache
            .RemoveByTagAsync(
                CacheKeyBuilder.TenantTag(integrationEvent.TenantId),
                cancellationToken)
            .ConfigureAwait(false);
        await cache
            .RemoveByTagAsync(
                CacheKeyBuilder.DomainTag(integrationEvent.Domain),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
