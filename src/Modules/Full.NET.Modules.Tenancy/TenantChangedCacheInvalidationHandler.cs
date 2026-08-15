using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy;

/// <summary>
/// 兼容 Outbox 排空：消费系统升级前已入库的 tenant.changed（租户名称/禁用/套餐变更）
/// 集成事件，并调用 TenantCacheInvalidator.InvalidateDistributedAsync 执行幂等 L2/Backplane 失效。
/// 新写入路径（HostTenantManagementService.Update/Disable/AssignPackage）已改为事务提交后
/// 直接失效，不再单独产生该缓存专用消息。
/// </summary>
internal sealed class TenantChangedCacheInvalidationHandler(
    IIntegrationEventSerializer serializer,
    TenantCacheInvalidator invalidator) : IIntegrationEventHandler
{
    /// <summary>标准事件类型名。</summary>
    public string EventType => "fullnet.tenancy.tenant.changed";

    /// <summary>无历史遗留事件类型。</summary>
    public IReadOnlyList<string> LegacyEventTypes => [];

    /// <summary>事件 Payload 模式版本。</summary>
    public int SchemaVersion => 1;

    /// <summary>
    /// 删除共享缓存并广播 Backplane 天然幂等；重复投递只会收敛到同一缓存缺失状态。
    /// </summary>
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
        IntegrationEventIdempotencyStrategy.NaturallyIdempotent;

    /// <summary>
    /// 反序列化 TenantChangedIntegrationEvent 并触发分布式缓存失效。异常向上抛出让调度器重试。
    /// </summary>
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
