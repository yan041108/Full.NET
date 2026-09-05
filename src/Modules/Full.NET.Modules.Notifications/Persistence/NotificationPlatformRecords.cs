namespace Full.NET.Modules.Notifications.Persistence;

/// <summary>通知模板持久化投影。</summary>
internal sealed record NotificationTemplateRecord(
    Guid Id,
    Guid? TenantId,
    string ScopeKey,
    string TenantScopeKey,
    string TemplateKey,
    string LocaleTag,
    string DefaultLocaleTag,
    string ChannelKey,
    string ContentCategoryKey,
    string DraftSubject,
    string DraftBodyJson,
    string DraftParameterSchemaJson,
    long DraftRevision,
    Guid? LatestPublishedVersionId,
    Guid CreatedById,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    long Version);

/// <summary>同一模板键下的语言变体摘要，用于发布选择与缺失提示。</summary>
internal sealed record NotificationTemplateLocaleStateRecord(
    Guid Id,
    string LocaleTag,
    string DefaultLocaleTag,
    Guid? LatestPublishedVersionId);

/// <summary>通知模板列表投影，一次查询携带最新发布版本摘要，避免逐行补查。</summary>
internal sealed record NotificationTemplateListRecord(
    Guid Id,
    Guid? TenantId,
    string ScopeKey,
    string TenantScopeKey,
    string TemplateKey,
    string LocaleTag,
    string DefaultLocaleTag,
    string ChannelKey,
    string ContentCategoryKey,
    string DraftSubject,
    string DraftBodyJson,
    string DraftParameterSchemaJson,
    long DraftRevision,
    Guid? LatestPublishedVersionId,
    int? LatestPublishedVersionNumber,
    string? LatestContentHash,
    string? LatestContentClassificationKey,
    Guid CreatedById,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    long Version);

/// <summary>不可变通知模板版本持久化投影。</summary>
internal sealed record NotificationTemplateVersionRecord(
    Guid Id,
    Guid TemplateId,
    string LocaleTag,
    int VersionNumber,
    int SchemaVersion,
    string Subject,
    string BodyJson,
    string ParameterSchemaJson,
    string ContentClassificationKey,
    string ContentHash,
    Guid PublishedById,
    DateTimeOffset PublishedAtUtc);

/// <summary>通知意图持久化投影。</summary>
internal sealed record NotificationIntentRecord(
    Guid Id,
    Guid? TenantId,
    string ScopeKey,
    string TenantScopeKey,
    string ProducerKey,
    string SceneKey,
    string IdempotencyKey,
    Guid TemplateVersionId,
    Guid? BindingVersionId,
    string PolicyCategoryKey,
    string DispatchModeKey,
    string RouteSnapshotJson,
    string ParameterSnapshotJson,
    string StatusKey,
    Guid CreatedById,
    DateTimeOffset CreatedAtUtc,
    long Revision);

/// <summary>通知收件人持久化投影。</summary>
internal sealed record NotificationRecipientRecord(
    Guid Id,
    Guid IntentId,
    string RecipientTypeKey,
    string RecipientKey,
    Guid? UserId,
    string? AddressDigest,
    string ResolutionStatusKey,
    DateTimeOffset CreatedAtUtc);

/// <summary>渠道投递持久化投影。</summary>
internal sealed record NotificationDeliveryRecord(
    Guid Id,
    Guid IntentId,
    Guid RecipientId,
    string ChannelKey,
    Guid? ProviderProfileVersionId,
    Guid? BindingVersionId,
    string StatusKey,
    long Revision,
    string? LeaseOwnerKey,
    DateTimeOffset? LeaseExpiresAtUtc,
    long LeaseGeneration,
    DateTimeOffset? NextAttemptAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

/// <summary>投递尝试持久化投影。</summary>
internal sealed record NotificationDeliveryAttemptRecord(
    Guid Id,
    Guid DeliveryId,
    int AttemptNumber,
    string? LeaseOwnerKey,
    long LeaseGeneration,
    DateTimeOffset? LeaseExpiresAtUtc,
    string? ResultCategoryKey,
    string StatusKey,
    string? ProviderMessageId,
    string? ErrorCode,
    string? ReceiptDigest,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc);

/// <summary>投递回执持久化投影。</summary>
internal sealed record NotificationReceiptRecord(
    Guid Id,
    string ProviderTypeKey,
    string? ProviderMessageId,
    string ReceiptIdempotencyKey,
    Guid? DeliveryId,
    string ExternalStatusKey,
    string MappedStatusKey,
    string PayloadDigest,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset? ProcessedAtUtc,
    string ProcessStatusKey);

/// <summary>渠道配置持久化投影；查询路径不读取 SecretReference 原文。</summary>
internal sealed record NotificationProviderProfileRecord(
    Guid Id,
    Guid? TenantId,
    string ScopeKey,
    string TenantScopeKey,
    string ProfileKey,
    string ProviderTypeKey,
    string NonSecretConfigJson,
    string? SecretReference,
    bool IsEnabled,
    long DraftRevision,
    Guid? LatestPublishedVersionId,
    Guid CreatedById,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    long Version);

/// <summary>不可变渠道配置版本持久化投影。</summary>
internal sealed record NotificationProviderProfileVersionRecord(
    Guid Id,
    Guid ProfileId,
    int VersionNumber,
    string ProviderTypeKey,
    string AdapterVersion,
    string NonSecretConfigJson,
    string? SecretReference,
    string ContentHash,
    Guid PublishedById,
    DateTimeOffset PublishedAtUtc);

/// <summary>场景绑定持久化投影。</summary>
internal sealed record NotificationBindingRecord(
    Guid Id,
    Guid? TenantId,
    string ScopeKey,
    string TenantScopeKey,
    string BindingKey,
    string DraftDispatchModeKey,
    string DraftJson,
    long DraftRevision,
    Guid? LatestPublishedVersionId,
    Guid CreatedById,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    long Version);

/// <summary>不可变场景绑定版本持久化投影。</summary>
internal sealed record NotificationBindingVersionRecord(
    Guid Id,
    Guid BindingId,
    int VersionNumber,
    string ProducerKey,
    string SceneKey,
    string ChannelKey,
    string DispatchModeKey,
    string BindingTargetsJson,
    string ContentHash,
    Guid PublishedById,
    DateTimeOffset PublishedAtUtc);
