using Full.NET.Abstractions.Messaging;
using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.Hosting;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.Modules.Tenancy;

internal sealed class TenantProvisionedCacheInvalidationHandler(
    IIntegrationEventSerializer serializer,
    IFusionCache cache,
    IHostEnvironment environment) : IIntegrationEventHandler
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
            .RemoveAsync(
                CacheKeyBuilder.TenantResolutionById(
                    environment.EnvironmentName,
                    integrationEvent.TenantId),
                token: cancellationToken)
            .ConfigureAwait(false);
        await cache
            .RemoveAsync(
                CacheKeyBuilder.TenantResolutionByDomain(
                    environment.EnvironmentName,
                    integrationEvent.Domain),
                token: cancellationToken)
            .ConfigureAwait(false);
        await cache
            .RemoveByTagAsync(
                CacheKeyBuilder.TenantTag(integrationEvent.TenantId),
                token: cancellationToken)
            .ConfigureAwait(false);
        await cache
            .RemoveByTagAsync(
                CacheKeyBuilder.DomainTag(integrationEvent.Domain),
                token: cancellationToken)
            .ConfigureAwait(false);
    }
}
