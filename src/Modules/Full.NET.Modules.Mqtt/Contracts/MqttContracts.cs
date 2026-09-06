namespace Full.NET.Modules.Mqtt.Contracts;

/// <summary>MQTT 控制面的稳定权限码。</summary>
public static class MqttPermissions
{
    /// <summary>允许查询 MQTT Broker 部署状态。</summary>
    public const string BrokerRead = "mqtt.broker.read";

    /// <summary>允许查询 MQTT 客户端目录。</summary>
    public const string ClientsRead = "mqtt.clients.read";

    /// <summary>允许查询 MQTT 消息记录。</summary>
    public const string MessagesRead = "mqtt.messages.read";

    /// <summary>允许在 ACL 与限额内发布 MQTT 消息。</summary>
    public const string MessagesPublish = "mqtt.messages.publish";
}

/// <summary>MQTT 消息发布状态机值。</summary>
public static class MqttMessageStatuses
{
    /// <summary>已登记，等待发布。</summary>
    public const string Pending = "pending";

    /// <summary>已成功发布到 Broker。</summary>
    public const string Published = "published";

    /// <summary>发布失败。</summary>
    public const string Failed = "failed";
}

/// <summary>MQTT 模块稳定错误码。</summary>
public static class MqttErrorCodes
{
    public const string Prefix = "mqtt.";

    public const string BrokerUnavailable = "mqtt.broker.unavailable";

    public const string ClientNotFound = "mqtt.client.not_found";

    public const string MessageNotFound = "mqtt.message.not_found";

    public const string TopicForbidden = "mqtt.topic.forbidden";

    public const string PayloadTooLarge = "mqtt.payload.too_large";

    public const string PublishRateLimited = "mqtt.publish.rate_limited";

    public const string IdempotencyConflict = "mqtt.publish.idempotency_conflict";

    public const string PublishValidationFailed = "mqtt.publish.validation_failed";
}

/// <summary>MQTT Broker 部署状态响应。</summary>
public sealed record MqttBrokerStatusResponse(
    bool IsEnabled,
    string Host,
    int Port,
    bool UseTls,
    int MaximumPayloadBytes,
    int MaximumPublishRatePerMinute,
    IReadOnlyList<string> AllowedPublishTopicPrefixes,
    string DeploymentNotice);

/// <summary>MQTT 客户端目录项。</summary>
public sealed record MqttClientResponse(
    Guid Id,
    string ClientKey,
    string DisplayName,
    string? Description,
    Guid? TenantId,
    bool IsEnabled,
    int SortOrder,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

/// <summary>MQTT 消息记录响应。</summary>
public sealed record MqttMessageResponse(
    Guid Id,
    Guid? TenantId,
    Guid? ClientId,
    string? ClientKey,
    string Topic,
    int PayloadSizeBytes,
    int Qos,
    string Status,
    string? IdempotencyKey,
    string? SummaryMessage,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId);

/// <summary>受控 MQTT 发布请求。</summary>
public sealed record PublishMqttMessageRequest(
    string Topic,
    string Payload,
    int Qos,
    Guid? ClientId,
    string? IdempotencyKey);

/// <summary>MQTT 消息列表过滤条件。</summary>
public sealed record MqttMessageListFilter(
    Guid? ClientId,
    string? Status,
    string? Topic,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc);
