namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 解析事件流的有效交付所有权：目录默认值叠加持久化切流记录。
/// </summary>
public interface IEffectiveEventDeliveryOwnerResolver
{
    /// <summary>
    /// 解析指定事件类型与 SchemaVersion 的有效交付所有权。
    /// </summary>
    /// <param name="eventType">事件类型稳定名。</param>
    /// <param name="schemaVersion">事件 Schema 版本号。</param>
    /// <param name="cancellationToken">用于取消解析的令牌。</param>
    /// <returns>异步结果；返回当前生效的交付所有权，包含默认所有者与切流覆盖信息。</returns>
    Task<EventDeliveryOwner> GetDeliveryOwnerAsync(
        string eventType,
        int schemaVersion,
        CancellationToken cancellationToken = default);
    /// <summary>一次读取当前持久化所有权快照，解析运维目录中的多个事件流。</summary>
    /// <param name="topics">受信任的静态事件流目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>事件类型和版本对应的有效所有者，不用于跨业务事务缓存所有权。</returns>
    Task<IReadOnlyDictionary<(string EventType, int SchemaVersion), EventDeliveryOwner>> GetDeliveryOwnersAsync(
        IReadOnlyCollection<IntegrationEventTopicDefinition> topics, CancellationToken cancellationToken = default);
}
