namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 声明编译期可生成的集成事件订阅路由元数据。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class IntegrationEventSubscriptionAttribute(
    string consumerName,
    string messageType,
    int schemaVersion) : Attribute
{
    /// <summary>
    /// 订阅所属的 Kafka Consumer Group 机器码。
    /// </summary>
    public string ConsumerName { get; } = consumerName;

    /// <summary>
    /// 稳定事件类型名（四段式 owner.module.entity.event）。
    /// </summary>
    public string MessageType { get; } = messageType;

    /// <summary>
    /// 事件契约 Schema 版本号，从 1 开始。
    /// </summary>
    public int SchemaVersion { get; } = schemaVersion;
}

/// <summary>
/// 编译期注册表返回的订阅描述；HandlerType 用于由当前 Scope 直接解析具体订阅。
/// </summary>
public readonly record struct IntegrationEventHandlerDescriptor(
    /// <summary>稳定事件类型名。</summary>
    string MessageType,
    /// <summary>Schema 版本号，从 1 开始。</summary>
    int SchemaVersion,
    /// <summary>订阅所属 Consumer Group 机器码。</summary>
    string ConsumerName,
    /// <summary>具体订阅实现的运行时类型，用于 Scoped 解析。</summary>
    Type HandlerType);

/// <summary>
/// 无反射的集成事件订阅注册表；每个业务程序集由生成器提供一个实现。
/// </summary>
public interface IIntegrationEventHandlerRegistry
{
    /// <summary>
    /// 按路由键查找编译期声明的订阅描述。
    /// </summary>
    /// <param name="messageType">稳定事件类型名。</param>
    /// <param name="schemaVersion">Schema 版本号。</param>
    /// <param name="consumerName">Consumer Group 机器码。</param>
    /// <param name="descriptor">找到时输出描述快照。</param>
    /// <returns>命中返回 true，否则 false。</returns>
    bool TryResolve(
        string messageType,
        int schemaVersion,
        string consumerName,
        out IntegrationEventHandlerDescriptor descriptor);
}
