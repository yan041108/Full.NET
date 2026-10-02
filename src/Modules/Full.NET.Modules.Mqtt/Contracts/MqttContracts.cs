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
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。AllowedPublishTopicPrefixes 为稳定 ACL 前缀，发布后不可改名或删除。</remarks>
/// <param name="IsEnabled">Broker 是否启用。</param>
/// <param name="Host">Broker 主机。</param>
/// <param name="Port">Broker 端口。</param>
/// <param name="UseTls">是否使用 TLS。</param>
/// <param name="MaximumPayloadBytes">单条消息最大字节数。</param>
/// <param name="MaximumPublishRatePerMinute">每分钟最大发布速率。</param>
/// <param name="AllowedPublishTopicPrefixes">允许发布的前缀列表；其他 topic 一律拒绝。</param>
/// <param name="DeploymentNotice">部署说明文本。</param>
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
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。ClientKey 为稳定机器码，发布后不可改名或删除。</remarks>
/// <param name="Id">客户端标识。</param>
/// <param name="ClientKey">稳定机器码；发布后不可改名或删除。</param>
/// <param name="DisplayName">展示名称。</param>
/// <param name="Description">描述；可为空。</param>
/// <param name="TenantId">归属租户；为空表示平台级客户端。</param>
/// <param name="IsEnabled">客户端是否启用。</param>
/// <param name="SortOrder">展示顺序，值小者靠前。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）；可为空。</param>
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
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。Status 为稳定机器码，取 <see cref="MqttMessageStatuses"/> 集合。</remarks>
/// <param name="Id">消息标识。</param>
/// <param name="TenantId">归属租户；为空表示平台级消息。</param>
/// <param name="ClientId">关联客户端标识；可为空。</param>
/// <param name="ClientKey">关联客户端稳定机器码；可为空。</param>
/// <param name="Topic">发布目标 topic。</param>
/// <param name="PayloadSizeBytes">载荷字节数。</param>
/// <param name="Qos">MQTT QoS 等级（0/1/2）。</param>
/// <param name="Status">发布状态稳定机器码，取 <see cref="MqttMessageStatuses"/> 集合。</param>
/// <param name="IdempotencyKey">幂等键；相同键重复发布被拒。</param>
/// <param name="SummaryMessage">失败原因或摘要；可为空。</param>
/// <param name="PublishedAtUtc">成功发布时间（UTC）；未发布为空。</param>
/// <param name="CreatedAtUtc">记录创建时间（UTC）。</param>
/// <param name="CreatedByUserId">发起用户标识。</param>
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
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。Topic 必须命中 ACL AllowedPublishTopicPrefixes。</remarks>
/// <param name="Topic">发布目标 topic；须通过 ACL 校验。</param>
/// <param name="Payload">载荷文本（UTF-8）。</param>
/// <param name="Qos">MQTT QoS 等级（0/1/2）。</param>
/// <param name="ClientId">指定客户端标识；为空由服务端选择默认客户端。</param>
/// <param name="IdempotencyKey">幂等键；相同键重复发布被拒。</param>
public sealed record PublishMqttMessageRequest(
    string Topic,
    string Payload,
    int Qos,
    Guid? ClientId,
    string? IdempotencyKey);

/// <summary>MQTT 消息列表过滤条件。</summary>
/// <param name="ClientId">按客户端过滤；为空表示不限。</param>
/// <param name="Status">按状态稳定机器码过滤，取 <see cref="MqttMessageStatuses"/> 集合；为空表示不限。</param>
/// <param name="Topic">按 topic 模糊匹配；为空表示不限。</param>
/// <param name="FromUtc">起始时间（UTC）；为空表示不限下界。</param>
/// <param name="ToUtc">结束时间（UTC）；为空表示不限上界。</param>
public sealed record MqttMessageListFilter(
    Guid? ClientId,
    string? Status,
    string? Topic,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc);
