using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy;

/// <summary>
/// 兼容排空：消费升级前已入库的租户变更 Outbox，并执行幂等缓存失效。
/// 新写入路径不再产生该消息。
/// </summary>
internal sealed class TenantChangedCacheInvalidationHandler(
    IIntegrationEventSerializer serializer,
    TenantCacheInvalidator invalidator) : IIntegrationEventHandler
{
    public string EventType => "fullnet.tenancy.tenant.changed";

    public IReadOnlyList<string> LegacyEventTypes => [];

    public int SchemaVersion => 1;

    // 删除共享缓存并广播失效可重复执行，重复投递只会收敛到同一缓存缺失状态。
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
        IntegrationEventIdempotencyStrategy.NaturallyIdempotent;

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
