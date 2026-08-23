using Full.NET.Messaging.Abstractions;

namespace Full.NET.Modules.Messaging;

/// <summary>
/// 解析事件流当前生效的交付所有者：优先采用已持久化的切流所有权，未持久化时回退到 Topic 目录静态配置。
/// </summary>
/// <remarks>
/// 业务事务写 Outbox 前必须调用本解析器确定发布链路，避免按瞬时配置造成双发布。
/// 迁移期 Topic 目录可能尚未登记既有可靠事件流，此时回退为 <see cref="EventDeliveryOwner.LegacyPolling"/>，
/// 保证未登记流仍由 Legacy Worker 发布，不在业务事务中因目录不完整而停止写 Outbox。
/// </remarks>
internal sealed class EffectiveEventDeliveryOwnerResolver(
    IntegrationEventSubscriptionCatalog catalog,
    IEventStreamOwnershipStore ownershipStore) : IEffectiveEventDeliveryOwnerResolver
{
    /// <summary>
    /// 返回指定事件流当前生效的交付所有者。
    /// </summary>
    /// <param name="eventType">稳定的事件类型标识。</param>
    /// <param name="schemaVersion">事件契约正整数版本。</param>
    /// <param name="cancellationToken">用于取消数据库操作的令牌。</param>
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
