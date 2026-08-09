using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;

namespace Full.NET.Data.Dapper.Outbox;

/// <summary>
/// 按事件流的有效交付所有权（LegacyPolling / ShadowCdc / CdcKafka）
/// 选择写入 fn_outbox_message 或 fn_messaging_outbox_event。
/// </summary>
/// <remarks>
/// 为什么业务模块不直接决定写哪张表：
/// 切流决策由 Messaging 模块集中管理（Topic 目录 + 持久化切流记录），
/// 业务代码只认识 IOutboxWriter 接口；路由切换不会引起业务模块重编译或重部署。
/// Legacy 与 Append-only 表在同一事务内保持互斥：一次业务调用只能命中其中一张，
/// 避免重复投递、顺序错乱和在 rollback 下两表都产生脏行。
/// 
/// 为什么不按 MessagingOutboxOptions.Mode 全局切换：
/// 同一 Worker 需要同时承载已切流事件（CdcKafka）与未切流事件（LegacyPolling），
/// 混合模式下全局二选一会导致至少一侧永远写不出去。
/// 配置保留一版，标记 obsolete 供配置迁移脚本参考。
/// </remarks>
internal sealed class DapperRoutedOutboxWriter(
    DapperOutboxWriter legacyWriter,
    DapperAppendOnlyOutboxWriter appendOnlyWriter,
    IEffectiveEventDeliveryOwnerResolver ownerResolver) : IOutboxWriter
{
    /// <summary>
    /// 无 metadata overload：LegacyPolling/ShadowCdc 直接写 legacy 表；
    /// CdcKafka 失败关闭——已切流的流必须显式携带 PartitionKey 与 Producer。
    /// </summary>
    public async Task AddAsync<TEvent>(
        string eventType,
        int schemaVersion,
        TEvent payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentNullException.ThrowIfNull(payload);

        var owner = await ownerResolver
            .GetDeliveryOwnerAsync(eventType, schemaVersion, cancellationToken)
            .ConfigureAwait(false);

        switch (owner)
        {
            case EventDeliveryOwner.LegacyPolling:
            case EventDeliveryOwner.ShadowCdc:
                // Shadow 阶段实际仍由 Legacy poller 产出；Shadow 的 append-only
                // 对比工作由独立 shadow writer 单独执行，不在业务事务路径内。
                await legacyWriter
                    .AddAsync(eventType, schemaVersion, payload, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case EventDeliveryOwner.CdcKafka:
                throw new InvalidOperationException(
                    $"Event stream ('{eventType}', schema {schemaVersion}) is owned " +
                    $"by {nameof(EventDeliveryOwner.CdcKafka)} but called with the " +
                    $"non-metadata overload. IntegrationEventMetadata (PartitionKey, " +
                    $"Producer, trace context) is required for append-only outbox " +
                    $"and CDC relay. Reason: " +
                    IntegrationEventFailureCodes.OutboxEventMetadataMissing);
            default:
                throw new InvalidOperationException(
                    $"Unknown event delivery owner '{owner}' for stream " +
                    $"('{eventType}', schema {schemaVersion}).");
        }
    }

    /// <summary>
    /// 带 metadata overload：LegacyPolling/ShadowCdc 丢弃 metadata 写 legacy 表；
    /// CdcKafka 写 append-only 表。业务模块始终只需调用本重载即可同时满足
    /// 切流前后两种模式，无需分支判断。
    /// </summary>
    public async Task AddAsync<TEvent>(
        string eventType,
        int schemaVersion,
        TEvent payload,
        IntegrationEventMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(metadata);

        var owner = await ownerResolver
            .GetDeliveryOwnerAsync(eventType, schemaVersion, cancellationToken)
            .ConfigureAwait(false);

        switch (owner)
        {
            case EventDeliveryOwner.LegacyPolling:
            case EventDeliveryOwner.ShadowCdc:
                // 切流前业务模块已经开始传 metadata 时不要报 NotSupportedException，
                // 直接忽略 metadata 并写 legacy 表。切流日仅靠 owner 切换生效。
                await legacyWriter
                    .AddAsync(eventType, schemaVersion, payload, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case EventDeliveryOwner.CdcKafka:
                await appendOnlyWriter
                    .AddAsync(
                        eventType,
                        schemaVersion,
                        payload,
                        metadata,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            default:
                throw new InvalidOperationException(
                    $"Unknown event delivery owner '{owner}' for stream " +
                    $"('{eventType}', schema {schemaVersion}).");
        }
    }
}
