namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>通知模板草稿与已发布版本的 HTTP 契约；Scope 只来自受信会话。</summary>
public sealed record NotificationTemplateBody(string Text);

/// <summary>闭合参数 Schema；未知类型、缺失上限或超限必须失败关闭。</summary>
public sealed record NotificationTemplateParameterSchema(
    int SchemaVersion,
    IReadOnlyList<NotificationTemplateParameterDefinition> Parameters);

/// <summary>单个模板参数定义；名称与类型为稳定机器码。</summary>
public sealed record NotificationTemplateParameterDefinition(
    string Name,
    string TypeKey,
    bool Required,
    int? MaxLength);

/// <summary>创建模板草稿；Channel 在本切片只允许 inbox。</summary>
public sealed record CreateNotificationTemplateRequest(
    string TemplateKey,
    string ChannelKey,
    string ContentCategoryKey,
    string DraftSubject,
    NotificationTemplateBody DraftBody,
    NotificationTemplateParameterSchema ParameterSchema,
    string? LocaleTag = null,
    string? DefaultLocaleTag = null);

/// <summary>更新草稿；<c>Version</c> 为模板行 CAS 期望值。</summary>
public sealed record UpdateNotificationTemplateRequest(
    string DraftSubject,
    NotificationTemplateBody DraftBody,
    NotificationTemplateParameterSchema ParameterSchema,
    long Version);

/// <summary>发布不可变版本；内容分级只在发布时冻结。</summary>
public sealed record PublishNotificationTemplateRequest(
    long Version,
    string ContentClassificationKey);

/// <summary>模板详情；已发布字段仅在存在 LatestPublishedVersion 时有值。</summary>
public sealed record NotificationTemplateResponse(
    Guid Id,
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
    IReadOnlyList<string> PublishedLocaleTags,
    IReadOnlyList<string> MissingLocaleTags,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    long Version);
