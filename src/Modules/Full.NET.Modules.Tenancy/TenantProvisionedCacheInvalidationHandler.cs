using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy;

internal sealed class TenantProvisionedCacheInvalidationHandler(
    IIntegrationEventSerializer serializer,
    TenantCacheInvalidator invalidator) : IIntegrationEventHandler
{
    private static readonly string[] LegacyEventTypesValue =
        ["fullnet.tenancy.tenant-provisioned"];

    public string EventType => "fullnet.tenancy.tenant.provisioned";

    public IReadOnlyList<string> LegacyEventTypes => LegacyEventTypesValue;

    public int SchemaVersion => 1;

    // 删除共享缓存并广播失效可重复执行，重复投递只会收敛到同一缓存缺失状态。
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
        IntegrationEventIdempotencyStrategy.NaturallyIdempotent;

    public async Task HandleAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var integrationEvent = serializer
            .Deserialize<TenantProvisionedIntegrationEvent>(payload);
        await invalidator.InvalidateDistributedAsync(
                integrationEvent.TenantId,
                integrationEvent.Domain,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
