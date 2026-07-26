using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy;

/// <summary>消费已提交的租户变更事实并可靠传播缓存失效。</summary>
internal sealed class TenantChangedCacheInvalidationHandler(
    IIntegrationEventSerializer serializer,
    TenantCacheInvalidator invalidator) : IIntegrationEventHandler
{
    public string EventType => "fullnet.tenancy.tenant.changed";

    public IReadOnlyList<string> LegacyEventTypes => [];

    public int SchemaVersion => 1;

    public Task HandleAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var integrationEvent = serializer
            .Deserialize<TenantChangedIntegrationEvent>(payload);
        return invalidator.InvalidateDistributedAsync(
            integrationEvent.TenantId,
            integrationEvent.Domain,
            cancellationToken);
    }
}
