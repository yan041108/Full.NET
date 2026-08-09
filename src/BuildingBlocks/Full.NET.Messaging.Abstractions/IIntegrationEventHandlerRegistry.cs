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
    public string ConsumerName { get; } = consumerName;

    public string MessageType { get; } = messageType;

    public int SchemaVersion { get; } = schemaVersion;
}

/// <summary>
/// 编译期注册表返回的订阅描述；HandlerType 用于由当前 Scope 直接解析具体订阅。
/// </summary>
public readonly record struct IntegrationEventHandlerDescriptor(
    string MessageType,
    int SchemaVersion,
    string ConsumerName,
    Type HandlerType);

/// <summary>
/// 无反射的集成事件订阅注册表；每个业务程序集由生成器提供一个实现。
/// </summary>
public interface IIntegrationEventHandlerRegistry
{
    bool TryResolve(
        string messageType,
        int schemaVersion,
        string consumerName,
        out IntegrationEventHandlerDescriptor descriptor);
}
