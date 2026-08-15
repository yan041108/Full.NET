namespace Full.NET.Data.Abstractions;

/// <summary>
/// 集成事件的载荷序列化/反序列化抽象，供 Outbox 写入端和 Inbox 消费端共享。
/// 实现必须保证跨版本的向后兼容性——旧版本写入的字节在新版本代码中可反序列化。
/// </summary>
/// <remarks>
/// <para>
/// 与至少一次投递 + Inbox 幂等的协同不变量：
/// <list type="bullet">
/// <item>
/// 同一 <typeparamref name="TEvent"/> 的序列化结果在语义等价的对象上必须产出
/// 完全相同的字节序列（确定性序列化），因为 Inbox 端会计算 SHA-256 PayloadHash
/// 作为幂等去重的第二维度（见 <see cref="InboxMessageFingerprint.PayloadHash"/>）。
/// JSON 序列化时需固定字段顺序、日期格式与空值处理策略，禁止使用非确定性字典。
/// </item>
/// <item>
/// ContentType 是持久化契约的一部分，变更时必须递增事件 SchemaVersion，并保留
/// 旧反序列化器至少一个部署周期，避免排空期内 Outbox 旧载荷无法读取。
/// </item>
/// </list>
/// </para>
/// </remarks>
public interface IIntegrationEventSerializer
{
    /// <summary>
    /// 获取当前序列化器的稳定线格式标识，用于写入 Outbox ContentType 列与
    /// Inbox 端兼容性校验。
    /// </summary>
    /// <remarks>
    /// 推荐格式："application/{format}[;{param}=value]"，例如
    /// "application/json;charset=utf-8;case=snake" 或
    /// "application/messagepack". 该值必须与 Deserialize 的输入 ContentType
    /// 严格匹配，不匹配时 Relay 会进入死信终态。
    /// </remarks>
    string ContentType { get; }

    /// <summary>
    /// 将事件载荷序列化为确定性字节数组，用于 Outbox 写入。
    /// </summary>
    /// <typeparam name="TEvent">事件载荷类型，通常为 record。</typeparam>
    /// <param name="payload">待序列化的事件载荷，不允许为 null。</param>
    /// <returns>
    /// 确定性字节数组：语义等价对象 → 相同字节，保证 PayloadHash 幂等键稳定。
    /// </returns>
    byte[] Serialize<TEvent>(TEvent payload);

    /// <summary>
    /// 从只读内存缓冲区反序列化事件载荷，用于 Inbox 消费端。
    /// </summary>
    /// <typeparam name="TEvent">目标载荷类型，由 SchemaVersion + MessageType 解析器路由。</typeparam>
    /// <param name="payload">Outbox.Payload 或 Broker 消息体的原始字节。</param>
    /// <returns>反序列化后的强类型对象；失败时抛出由实现定义的异常，由 Relay 转换为死信。</returns>
    TEvent Deserialize<TEvent>(ReadOnlyMemory<byte> payload);
}
