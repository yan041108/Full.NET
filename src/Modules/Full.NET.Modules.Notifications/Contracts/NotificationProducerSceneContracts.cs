namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>
/// 受控 Producer/Scene 目录详情；政策类别、合法渠道与紧急规则以目录为唯一权威，
/// 不允许仅通过可编辑模板把普通消息提升为强制通知。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">目录项唯一标识。</param>
/// <param name="ProducerKey">产生业务事实的生产者稳定键。</param>
/// <param name="SceneKey">触发通知的业务场景稳定键。</param>
/// <param name="PolicyCategoryKey">目录声明的政策类别键（mandatory/transactional/informational/marketing）。</param>
/// <param name="AllowedChannelKeys">目录声明的合法渠道键集合；意图渠道必须在其中。</param>
/// <param name="EmergencyOverrideAllowed">是否允许在静默时段对强制场景紧急覆盖。</param>
/// <param name="IsEnabled">目录项是否启用；停用后新意图失败关闭。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）。</param>
/// <param name="Version">CAS 乐观并发版本号；用于更新与启停请求的期望值。</param>
public sealed record NotificationProducerSceneResponse(
    Guid Id,
    string ProducerKey,
    string SceneKey,
    string PolicyCategoryKey,
    IReadOnlyList<string> AllowedChannelKeys,
    bool EmergencyOverrideAllowed,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version);

/// <summary>创建受控 Producer/Scene 目录项；分类与合法渠道按闭合集合校验。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="ProducerKey">生产者稳定键；同一作用域内与场景键共同唯一。</param>
/// <param name="SceneKey">业务场景稳定键。</param>
/// <param name="PolicyCategoryKey">政策类别键。</param>
/// <param name="AllowedChannelKeys">合法渠道键集合；不能为空且必须来自已登记渠道。</param>
/// <param name="EmergencyOverrideAllowed">是否允许静默时段紧急覆盖。</param>
public sealed record CreateNotificationProducerSceneRequest(
    string ProducerKey,
    string SceneKey,
    string PolicyCategoryKey,
    IReadOnlyList<string> AllowedChannelKeys,
    bool EmergencyOverrideAllowed);

/// <summary>更新受控 Producer/Scene 目录项；生产者与场景键不可改名。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="PolicyCategoryKey">政策类别键。</param>
/// <param name="AllowedChannelKeys">合法渠道键集合；不能为空。</param>
/// <param name="EmergencyOverrideAllowed">是否允许静默时段紧急覆盖。</param>
/// <param name="Version">CAS 乐观并发期望值；必须等于当前版本。</param>
public sealed record UpdateNotificationProducerSceneRequest(
    string PolicyCategoryKey,
    IReadOnlyList<string> AllowedChannelKeys,
    bool EmergencyOverrideAllowed,
    long Version);

/// <summary>启用或停用受控 Producer/Scene 目录项。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="IsEnabled">目标启用状态。</param>
/// <param name="Version">CAS 乐观并发期望值；必须等于当前版本。</param>
public sealed record SetNotificationProducerSceneEnabledRequest(
    bool IsEnabled,
    long Version);
