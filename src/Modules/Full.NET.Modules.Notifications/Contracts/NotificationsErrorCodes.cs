namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>
/// Notifications 模块稳定错误码集合，作为机器契约不可本地化。
/// </summary>
public static class NotificationsErrorCodes
{
    /// <summary>Notifications 错误码前缀。</summary>
    public const string Prefix = "notifications.";

    /// <summary>公告未找到。</summary>
    public const string AnnouncementNotFound = "notifications.announcement_not_found";

    /// <summary>公告乐观版本号或状态不符，CAS 并发控制失败。</summary>
    public const string AnnouncementConcurrencyConflict = "notifications.announcement_concurrency_conflict";

    /// <summary>公告状态不允许当前操作（仅草稿可更新或发布）。</summary>
    public const string AnnouncementInvalidStatus = "notifications.announcement_invalid_status";

    /// <summary>公告标题或正文长度校验失败。</summary>
    public const string AnnouncementValidationFailed = "notifications.announcement_validation_failed";

    public const string AnnouncementAudienceInvalid = "notifications.announcement_audience_invalid";

    /// <summary>站内信未找到。</summary>
    public const string InboxMessageNotFound = "notifications.inbox_message_not_found";

    /// <summary>站内信收件人不存在或非活动 Host 用户。</summary>
    public const string InboxRecipientNotFound = "notifications.inbox_recipient_not_found";

    /// <summary>站内信标题或正文长度校验失败。</summary>
    public const string InboxValidationFailed = "notifications.inbox_validation_failed";

    /// <summary>当前会话作用域不允许该站内信发送路径。</summary>
    public const string InboxScopeForbidden = "notifications.inbox_scope_forbidden";

    /// <summary>收件端点原值或类型校验失败。</summary>
    public const string RecipientEndpointValidationFailed = "notifications.recipient_endpoint_validation_failed";

    /// <summary>当前作用域和用户未找到指定收件端点。</summary>
    public const string RecipientEndpointNotFound = "notifications.recipient_endpoint_not_found";

    /// <summary>同一用户、渠道配置版本和端点类型已经登记。</summary>
    public const string RecipientEndpointConflict = "notifications.recipient_endpoint_conflict";

    /// <summary>政策求值后消息被抑制，不得创建外部 Delivery。</summary>
    public const string PolicySuppressed = "notifications.policy.suppressed";

    /// <summary>营销消息缺少明确同意，即使紧急覆盖也不得强行开启。</summary>
    public const string PolicyMarketingConsentRequired = "notifications.policy.marketing_consent_required";

    /// <summary>Single 路由没有恰好一个可用 Profile。</summary>
    public const string RouteProfileUnavailable = "notifications.route.profile_unavailable";

    /// <summary>FanOut 显式列表中没有启用的目标。</summary>
    public const string RouteFanOutEmpty = "notifications.route.fanout_empty";

    /// <summary>Failover 遇到永久错误，禁止换厂商重试。</summary>
    public const string RouteFailoverPermanent = "notifications.route.failover_permanent";

    /// <summary>Failover 已没有下一个可用 Profile。</summary>
    public const string RouteFailoverExhausted = "notifications.route.failover_exhausted";

    /// <summary>Match 没有命中任何 Profile。</summary>
    public const string RouteMatchNone = "notifications.route.match_none";

    /// <summary>Match 命中多个 Profile，必须失败关闭。</summary>
    public const string RouteMatchAmbiguous = "notifications.route.match_ambiguous";

    /// <summary>当前状态不允许该变迁，乱序回执不得回退终态。</summary>
    public const string DeliveryTransitionIllegal = "notifications.delivery.transition_illegal";

    /// <summary>非可信来源不得把投递标记为 Delivered。</summary>
    public const string DeliveryUntrustedDelivered = "notifications.delivery.untrusted_delivered";

    /// <summary>当前作用域未找到该投递。</summary>
    public const string DeliveryNotFound = "notifications.delivery_not_found";

    /// <summary>人工重试 CAS 失败或状态不允许重试。</summary>
    public const string DeliveryRetryConflict = "notifications.delivery_retry_conflict";

    /// <summary>人工重试理由不是短稳定文本。</summary>
    public const string DeliveryRetryInvalid = "notifications.delivery_retry_invalid";

    /// <summary>回执验签失败或载荷不是闭合字段。</summary>
    public const string ReceiptInvalid = "notifications.receipt_invalid";

    /// <summary>回执路径上的 ProviderType 没有登记验签器。</summary>
    public const string ReceiptProviderUnknown = "notifications.receipt_provider_unknown";

    /// <summary>回执原始 Body 超过允许大小。</summary>
    public const string ReceiptTooLarge = "notifications.receipt_too_large";

    /// <summary>当前作用域未找到该通知模板。</summary>
    public const string TemplateNotFound = "notifications.template_not_found";

    /// <summary>模板尚未发布不可变版本，不得创建 Intent。</summary>
    public const string TemplateNotPublished = "notifications.template_not_published";

    /// <summary>模板草稿、占位符或内容分级校验失败。</summary>
    public const string TemplateValidationFailed = "notifications.template_validation_failed";

    /// <summary>模板乐观版本号不符，CAS 并发控制失败。</summary>
    public const string TemplateConcurrencyConflict = "notifications.template_concurrency_conflict";

    /// <summary>同一作用域下 TemplateKey 已存在。</summary>
    public const string TemplateKeyConflict = "notifications.template_key_conflict";

    /// <summary>Intent 参数与已发布 Schema 不匹配。</summary>
    public const string TemplateParameterInvalid = "notifications.template_parameter_invalid";

    /// <summary>当前作用域未找到该通知意图。</summary>
    public const string IntentNotFound = "notifications.intent_not_found";

    /// <summary>同一幂等键已绑定不同载荷。</summary>
    public const string IntentIdempotencyConflict = "notifications.intent_idempotency_conflict";

    /// <summary>本切片只允许 inbox 渠道，其它渠道失败关闭。</summary>
    public const string IntentChannelUnsupported = "notifications.intent_channel_unsupported";

    /// <summary>收件人数量、去重或类型不满足闭合上限。</summary>
    public const string IntentRecipientLimit = "notifications.intent_recipient_limit";

    /// <summary>ProviderType 不在闭合目录中。</summary>
    public const string ProviderTypeUnknown = "notifications.provider_type_unknown";

    /// <summary>当前作用域未找到该渠道配置。</summary>
    public const string ProviderProfileNotFound = "notifications.provider_profile_not_found";

    /// <summary>同一作用域下 ProfileKey 已存在。</summary>
    public const string ProviderProfileKeyConflict = "notifications.provider_profile_key_conflict";

    /// <summary>渠道配置乐观版本号不符。</summary>
    public const string ProviderProfileConcurrencyConflict = "notifications.provider_profile_concurrency_conflict";

    /// <summary>非密钥配置、Secret Reference 或 Schema 校验失败。</summary>
    public const string ProviderProfileValidationFailed = "notifications.provider_profile_validation_failed";

    /// <summary>渠道配置尚未发布不可变版本。</summary>
    public const string ProviderProfileNotPublished = "notifications.provider_profile_not_published";

    /// <summary>当前作用域未找到该场景绑定。</summary>
    public const string BindingNotFound = "notifications.binding_not_found";

    /// <summary>同一作用域下 BindingKey 已存在。</summary>
    public const string BindingKeyConflict = "notifications.binding_key_conflict";

    /// <summary>场景绑定乐观版本号不符。</summary>
    public const string BindingConcurrencyConflict = "notifications.binding_concurrency_conflict";

    /// <summary>绑定草稿、目标或作用域校验失败。</summary>
    public const string BindingValidationFailed = "notifications.binding_validation_failed";

    /// <summary>意图渠道没有已发布绑定。</summary>
    public const string BindingNotPublished = "notifications.binding_not_published";

    /// <summary>同一作用域下 Producer/Scene/Channel 已有已发布绑定。</summary>
    public const string BindingSceneConflict = "notifications.binding_scene_conflict";

    /// <summary>已登记的全部错误码，供治理与多语言资源完整性校验使用。</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        AnnouncementNotFound,
        AnnouncementConcurrencyConflict,
        AnnouncementInvalidStatus,
        AnnouncementValidationFailed,
        AnnouncementAudienceInvalid,
        InboxMessageNotFound,
        InboxRecipientNotFound,
        InboxValidationFailed,
        InboxScopeForbidden,
        RecipientEndpointValidationFailed,
        RecipientEndpointNotFound,
        RecipientEndpointConflict,
        PolicySuppressed,
        PolicyMarketingConsentRequired,
        RouteProfileUnavailable,
        RouteFanOutEmpty,
        RouteFailoverPermanent,
        RouteFailoverExhausted,
        RouteMatchNone,
        RouteMatchAmbiguous,
        DeliveryTransitionIllegal,
        DeliveryUntrustedDelivered,
        DeliveryNotFound,
        DeliveryRetryConflict,
        DeliveryRetryInvalid,
        ReceiptInvalid,
        ReceiptProviderUnknown,
        ReceiptTooLarge,
        TemplateNotFound,
        TemplateNotPublished,
        TemplateValidationFailed,
        TemplateConcurrencyConflict,
        TemplateKeyConflict,
        TemplateParameterInvalid,
        IntentNotFound,
        IntentIdempotencyConflict,
        IntentChannelUnsupported,
        IntentRecipientLimit,
        ProviderTypeUnknown,
        ProviderProfileNotFound,
        ProviderProfileKeyConflict,
        ProviderProfileConcurrencyConflict,
        ProviderProfileValidationFailed,
        ProviderProfileNotPublished,
        BindingNotFound,
        BindingKeyConflict,
        BindingConcurrencyConflict,
        BindingValidationFailed,
        BindingNotPublished,
        BindingSceneConflict,
    ]);
}
