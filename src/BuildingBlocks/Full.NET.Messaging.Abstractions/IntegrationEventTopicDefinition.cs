namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 版本化 Topic 目录条目；绑定稳定 <see cref="TopicCode"/>、事件契约与发布所有权。
/// </summary>
public sealed class IntegrationEventTopicDefinition
{
    /// <summary>
    /// Topic 稳定机器码；由模块前缀与事件名组成，发布后不可改名，作为发布与订阅的解析键。
    /// </summary>
    public string TopicCode { get; }

    /// <summary>
    /// 事件契约的稳定消息类型全名（如领域.模块.事件.版本）；与信封 MessageType 一致。
    /// </summary>
    public string EventType { get; }

    /// <summary>
    /// 事件契约的 Schema 版本号；从 1 开始递增，同 TopicCode 下多个版本共存。
    /// </summary>
    public int SchemaVersion { get; }

    /// <summary>
    /// 投递所有权归属；标识由谁负责把事件发布到 Topic，避免多模块重复投递。
    /// </summary>
    public EventDeliveryOwner DeliveryOwner { get; }

    private IntegrationEventTopicDefinition(
        string topicCode,
        string eventType,
        int schemaVersion,
        EventDeliveryOwner deliveryOwner)
    {
        TopicCode = topicCode;
        EventType = eventType;
        SchemaVersion = schemaVersion;
        DeliveryOwner = deliveryOwner;
    }

    /// <summary>
    /// 校验并构造 Topic 目录条目。
    /// </summary>
    public static IntegrationEventTopicDefinition Create(
        string topicCode,
        string eventType,
        int schemaVersion,
        EventDeliveryOwner deliveryOwner)
    {
        ValidateTopicCode(topicCode);
        IntegrationEventEnvelope.ValidateMessageType(eventType);
        IntegrationEventEnvelope.ValidateSchemaVersion(schemaVersion);

        return new IntegrationEventTopicDefinition(
            topicCode,
            eventType,
            schemaVersion,
            deliveryOwner);
    }

    internal static void ValidateTopicCode(string topicCode)
    {
        if (string.IsNullOrWhiteSpace(topicCode)
            || !MessagingNames.TopicCodePattern.IsMatch(topicCode))
        {
            throw new ArgumentException(
                IntegrationEventFailureCodes.TopicCodeInvalid,
                nameof(topicCode));
        }
    }
}