namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>绑定草稿中的显式 Profile 目标；优先级由 Order 决定。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="ProfileKey">绑定目标 Profile 的稳定键；发布后不可改名。</param>
/// <param name="Order">同一绑定内多个 Profile 的派发优先级；数值越小越优先。</param>
public sealed record NotificationBindingTargetInput(
    string ProfileKey,
    int Order);

/// <summary>创建场景绑定草稿；Enabled Profile 不会自动进入目标列表。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="BindingKey">绑定稳定键；同一租户内唯一，发布后不可改名。</param>
/// <param name="DispatchModeKey">派发模式键（如即时、批量、延迟）。</param>
/// <param name="ProducerKey">产生事件的生产者键。</param>
/// <param name="SceneKey">触发通知的业务场景键。</param>
/// <param name="ChannelKey">通知渠道键（如邮件、短信、站内信）。</param>
/// <param name="Targets">显式 Profile 目标列表；不能为空，Enabled Profile 不会自动加入。</param>
public sealed record CreateNotificationBindingRequest(
    string BindingKey,
    string DispatchModeKey,
    string ProducerKey,
    string SceneKey,
    string ChannelKey,
    IReadOnlyList<NotificationBindingTargetInput> Targets);

/// <summary>更新绑定草稿；<c>Version</c> 为 CAS 期望值。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="DispatchModeKey">派发模式键（如即时、批量、延迟）。</param>
/// <param name="ProducerKey">产生事件的生产者键。</param>
/// <param name="SceneKey">触发通知的业务场景键。</param>
/// <param name="ChannelKey">通知渠道键（如邮件、短信、站内信）。</param>
/// <param name="Targets">显式 Profile 目标列表；不能为空。</param>
/// <param name="Version">CAS 乐观并发期望值；必须等于当前草稿版本，否则更新失败。</param>
public sealed record UpdateNotificationBindingRequest(
    string DispatchModeKey,
    string ProducerKey,
    string SceneKey,
    string ChannelKey,
    IReadOnlyList<NotificationBindingTargetInput> Targets,
    long Version);

/// <summary>发布不可变绑定版本；引用的 Profile 必须已启用且已发布。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Version">CAS 乐观并发期望值；必须等于当前草稿版本，发布成功后生成不可变版本。</param>
public sealed record PublishNotificationBindingRequest(long Version);

/// <summary>场景绑定详情；已发布字段仅在存在 LatestPublishedVersion 时有值。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">绑定唯一标识。</param>
/// <param name="BindingKey">绑定稳定键；发布后不可改名。</param>
/// <param name="DraftDispatchModeKey">草稿派发模式键。</param>
/// <param name="DraftJson">草稿原始 JSON 内容；用于编辑回显，不保证结构稳定。</param>
/// <param name="DraftRevision">草稿内部修订号；仅用于调试，不作为并发版本。</param>
/// <param name="LatestPublishedVersionId">最近一次发布版本标识；未发布时为 <see langword="null"/>。</param>
/// <param name="LatestPublishedVersionNumber">最近发布版本号；未发布时为 <see langword="null"/>。</param>
/// <param name="LatestProducerKey">最近发布版本的生产者键；未发布时为 <see langword="null"/>。</param>
/// <param name="LatestSceneKey">最近发布版本的场景键；未发布时为 <see langword="null"/>。</param>
/// <param name="LatestChannelKey">最近发布版本的渠道键；未发布时为 <see langword="null"/>。</param>
/// <param name="LatestDispatchModeKey">最近发布版本的派发模式键；未发布时为 <see langword="null"/>。</param>
/// <param name="LatestBindingTargetsJson">最近发布版本的目标列表 JSON；未发布时为 <see langword="null"/>。</param>
/// <param name="CreatedAtUtc">绑定创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">绑定最近更新时间（UTC）；未更新时为 <see langword="null"/>。</param>
/// <param name="Version">CAS 乐观并发版本号；用于更新和发布请求的期望值。</param>
public sealed record NotificationBindingResponse(
    Guid Id,
    string BindingKey,
    string DraftDispatchModeKey,
    string DraftJson,
    long DraftRevision,
    Guid? LatestPublishedVersionId,
    int? LatestPublishedVersionNumber,
    string? LatestProducerKey,
    string? LatestSceneKey,
    string? LatestChannelKey,
    string? LatestDispatchModeKey,
    string? LatestBindingTargetsJson,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    long Version);
