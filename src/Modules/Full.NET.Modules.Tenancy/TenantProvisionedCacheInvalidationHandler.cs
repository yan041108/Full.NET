using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy;

/// <summary>
/// 兼容 Outbox 排空：消费系统升级前已入库的 tenant-provisioned / tenant.provisioned
/// 集成事件，并调用 TenantCacheInvalidator.InvalidateDistributedAsync 执行幂等 L2/Backplane 失效。
/// 新开通写入路径（TenantProvisioningService）已改为事务提交后直接失效，不再单独产生该缓存专用事件。
/// 幂等性：删除共享缓存并广播 Backplane 天然幂等，重复投递只会收敛到同一缓存缺失状态。
/// </summary>
internal sealed class TenantProvisionedCacheInvalidationHandler(
    IIntegrationEventSerializer serializer,
    TenantCacheInvalidator invalidator) : IIntegrationEventHandler
{
    private static readonly string[] LegacyEventTypesValue =
        ["fullnet.tenancy.tenant-provisioned"];

    /// <summary>当前版本标准事件类型名。</summary>
    public string EventType => "fullnet.tenancy.tenant.provisioned";

    /// <summary>历史遗留事件类型名列表，用于平滑升级期兼容旧 Outbox。</summary>
    public IReadOnlyList<string> LegacyEventTypes => LegacyEventTypesValue;

    /// <summary>事件 Payload 模式版本。</summary>
    public int SchemaVersion => 1;

    /// <summary>
    /// 删除共享缓存并广播失效可重复执行，重复投递只会收敛到同一缓存缺失状态。
    /// </summary>
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
        IntegrationEventIdempotencyStrategy.NaturallyIdempotent;

    /// <summary>
    /// 反序列化 TenantProvisionedIntegrationEvent 并触发分布式缓存失效。异常向上抛出让调度器重试。
    /// </summary>
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
