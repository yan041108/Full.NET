namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 版本化 Topic 目录条目；绑定稳定 <see cref="TopicCode"/>、事件契约与发布所有权。
/// </summary>
public sealed class IntegrationEventTopicDefinition
{
    public string TopicCode { get; }

    public string EventType { get; }

    public int SchemaVersion { get; }

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