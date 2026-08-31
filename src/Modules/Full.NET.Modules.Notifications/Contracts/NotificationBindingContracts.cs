namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>绑定草稿中的显式 Profile 目标；优先级由 Order 决定。</summary>
public sealed record NotificationBindingTargetInput(
    string ProfileKey,
    int Order);

/// <summary>创建场景绑定草稿；Enabled Profile 不会自动进入目标列表。</summary>
public sealed record CreateNotificationBindingRequest(
    string BindingKey,
    string DispatchModeKey,
    string ProducerKey,
    string SceneKey,
    string ChannelKey,
    IReadOnlyList<NotificationBindingTargetInput> Targets);

/// <summary>更新绑定草稿；<c>Version</c> 为 CAS 期望值。</summary>
public sealed record UpdateNotificationBindingRequest(
    string DispatchModeKey,
    string ProducerKey,
    string SceneKey,
    string ChannelKey,
    IReadOnlyList<NotificationBindingTargetInput> Targets,
    long Version);

/// <summary>发布不可变绑定版本；引用的 Profile 必须已启用且已发布。</summary>
public sealed record PublishNotificationBindingRequest(long Version);

/// <summary>场景绑定详情；已发布字段仅在存在 LatestPublishedVersion 时有值。</summary>
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
