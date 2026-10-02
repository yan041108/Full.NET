namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>通知模板草稿与已发布版本的 HTTP 契约；Scope 只来自受信会话。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Text">模板正文文本。</param>
public sealed record NotificationTemplateBody(string Text);

/// <summary>闭合参数 Schema；未知类型、缺失上限或超限必须失败关闭。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="SchemaVersion">Schema 版本号；版本不一致视为不兼容。</param>
/// <param name="Parameters">参数定义集合；顺序为模板渲染入参顺序。</param>
public sealed record NotificationTemplateParameterSchema(
    int SchemaVersion,
    IReadOnlyList<NotificationTemplateParameterDefinition> Parameters);

/// <summary>单个模板参数定义；名称与类型为稳定机器码。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。Name 与 TypeKey 为稳定机器码，发布后不可改名或删除。</remarks>
/// <param name="Name">参数名稳定机器码；与模板渲染入参一一对应。</param>
/// <param name="TypeKey">参数类型稳定机器码；未知类型必须失败关闭。</param>
/// <param name="Required">是否必填；缺失必填参数渲染前拒绝。</param>
/// <param name="MaxLength">字符串最大长度；非字符串类型或无限制时为空。</param>
public sealed record NotificationTemplateParameterDefinition(
    string Name,
    string TypeKey,
    bool Required,
    int? MaxLength);

/// <summary>创建模板草稿；Channel 在本切片只允许 inbox。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。TemplateKey、ChannelKey、ContentCategoryKey 为稳定机器码，发布后不可改名或删除。</remarks>
/// <param name="TemplateKey">模板稳定机器码；发布后不可改名或删除。</param>
/// <param name="ChannelKey">渠道稳定机器码；本切片仅接受 inbox。</param>
/// <param name="ContentCategoryKey">内容分级稳定机器码；用于内容合规分类。</param>
/// <param name="DraftSubject">草稿主题文本。</param>
/// <param name="DraftBody">草稿正文。</param>
/// <param name="ParameterSchema">闭合参数 Schema；未知类型必须失败关闭。</param>
/// <param name="LocaleTag">本草稿对应的 BCP 47 语言标签；为空使用默认。</param>
/// <param name="DefaultLocaleTag">模板默认语言 BCP 47 标签；为空使用系统默认。</param>
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
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="DraftSubject">草稿主题文本。</param>
/// <param name="DraftBody">草稿正文。</param>
/// <param name="ParameterSchema">闭合参数 Schema；未知类型必须失败关闭。</param>
/// <param name="Version">乐观锁版本号，用于 CAS 并发控制。</param>
public sealed record UpdateNotificationTemplateRequest(
    string DraftSubject,
    NotificationTemplateBody DraftBody,
    NotificationTemplateParameterSchema ParameterSchema,
    long Version);

/// <summary>发布不可变版本；内容分级只在发布时冻结。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。ContentClassificationKey 为稳定机器码，发布后不可改名或删除。</remarks>
/// <param name="Version">乐观锁版本号，用于 CAS 并发控制。</param>
/// <param name="ContentClassificationKey">内容分级稳定机器码；发布后冻结不可改。</param>
public sealed record PublishNotificationTemplateRequest(
    long Version,
    string ContentClassificationKey);

/// <summary>模板详情；已发布字段仅在存在 LatestPublishedVersion 时有值。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。TemplateKey、ChannelKey、ContentCategoryKey、LatestContentClassificationKey 为稳定机器码，发布后不可改名或删除。</remarks>
/// <param name="Id">模板标识。</param>
/// <param name="TemplateKey">模板稳定机器码；发布后不可改名或删除。</param>
/// <param name="LocaleTag">本草稿 BCP 47 语言标签。</param>
/// <param name="DefaultLocaleTag">模板默认语言 BCP 47 标签。</param>
/// <param name="ChannelKey">渠道稳定机器码。</param>
/// <param name="ContentCategoryKey">内容分级稳定机器码。</param>
/// <param name="DraftSubject">草稿主题文本。</param>
/// <param name="DraftBodyJson">草稿正文 JSON 序列化结果。</param>
/// <param name="DraftParameterSchemaJson">草稿参数 Schema JSON 序列化结果。</param>
/// <param name="DraftRevision">草稿修订号；每次更新自增。</param>
/// <param name="LatestPublishedVersionId">最近一次发布版本标识；未发布为空。</param>
/// <param name="LatestPublishedVersionNumber">最近一次发布版本号；未发布为空。</param>
/// <param name="LatestContentHash">最近一次发布内容 Hash；用于幂等比对。</param>
/// <param name="LatestContentClassificationKey">最近一次发布内容分级稳定机器码；未发布为空。</param>
/// <param name="PublishedLocaleTags">已发布覆盖的语言标签集合。</param>
/// <param name="MissingLocaleTags">尚未发布覆盖的语言标签集合。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）；可为空。</param>
/// <param name="Version">乐观锁版本号。</param>
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
