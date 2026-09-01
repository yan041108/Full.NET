namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>
/// 通知平台模板、渠道配置、绑定、投递与偏好的独立稳定权限码。
/// </summary>
/// <remarks>
/// 每个页面权限与每个写操作必须单独成码，禁止把 <c>read/update</c> 这类斜杠组合当作单一权限。
/// 现有公告与站内信权限保持兼容，不在此集合中重复。
/// </remarks>
public static class NotificationPlatformPermissions
{
    /// <summary>读取通知模板列表与详情。</summary>
    public const string TemplatesRead = "notifications.templates.read";
    /// <summary>创建新的通知模板。</summary>
    public const string TemplatesCreate = "notifications.templates.create";
    /// <summary>修改现有通知模板的内容与绑定。</summary>
    public const string TemplatesUpdate = "notifications.templates.update";
    /// <summary>发布通知模板到生产可用状态。</summary>
    public const string TemplatesPublish = "notifications.templates.publish";
    /// <summary>读取投递意图列表与详情。</summary>
    public const string IntentsRead = "notifications.intents.read";
    /// <summary>创建新的投递意图与默认偏好。</summary>
    public const string IntentsCreate = "notifications.intents.create";
    /// <summary>读取渠道提供方配置文件。</summary>
    public const string ProviderProfilesRead = "notifications.provider_profiles.read";
    /// <summary>创建新的渠道提供方配置。</summary>
    public const string ProviderProfilesCreate = "notifications.provider_profiles.create";
    /// <summary>修改渠道提供方的连接参数与凭据引用。</summary>
    public const string ProviderProfilesUpdate = "notifications.provider_profiles.update";
    /// <summary>发布渠道提供方配置，允许用于生产投递。</summary>
    public const string ProviderProfilesPublish = "notifications.provider_profiles.publish";
    /// <summary>启用渠道提供方配置，进入可投递状态。</summary>
    public const string ProviderProfilesEnable = "notifications.provider_profiles.enable";
    /// <summary>停用渠道提供方配置，新投递切换到备用渠道。</summary>
    public const string ProviderProfilesDisable = "notifications.provider_profiles.disable";
    /// <summary>读取意图绑定列表与详情。</summary>
    public const string BindingsRead = "notifications.bindings.read";
    /// <summary>创建新的意图与渠道/模板绑定关系。</summary>
    public const string BindingsCreate = "notifications.bindings.create";
    /// <summary>修改现有绑定的优先级、回退关系与过滤条件。</summary>
    public const string BindingsUpdate = "notifications.bindings.update";
    /// <summary>发布绑定配置到生产可用状态。</summary>
    public const string BindingsPublish = "notifications.bindings.publish";
    /// <summary>读取投递记录列表与详情。</summary>
    public const string DeliveriesRead = "notifications.deliveries.read";
    /// <summary>对失败投递发起一次人工重试。</summary>
    public const string DeliveriesRetry = "notifications.deliveries.retry";
    /// <summary>将终端失败投递转为死信，等待后续批处理或人工介入。</summary>
    public const string DeliveriesDeadLetter = "notifications.deliveries.dead_letter";
    /// <summary>读取用户或租户的通知偏好。</summary>
    public const string PreferencesRead = "notifications.preferences.read";
    /// <summary>更新自身用户的通知偏好。</summary>
    public const string PreferencesUpdate = "notifications.preferences.update";
    /// <summary>管理员代为管理或重置任意用户的通知偏好。</summary>
    public const string PreferencesManage = "notifications.preferences.manage";

    /// <summary>当前版本已注册的全部稳定权限码集合；顺序作为枚举列表的稳定投影。</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        TemplatesRead,
        TemplatesCreate,
        TemplatesUpdate,
        TemplatesPublish,
        IntentsRead,
        IntentsCreate,
        ProviderProfilesRead,
        ProviderProfilesCreate,
        ProviderProfilesUpdate,
        ProviderProfilesPublish,
        ProviderProfilesEnable,
        ProviderProfilesDisable,
        BindingsRead,
        BindingsCreate,
        BindingsUpdate,
        BindingsPublish,
        DeliveriesRead,
        DeliveriesRetry,
        DeliveriesDeadLetter,
        PreferencesRead,
        PreferencesUpdate,
        PreferencesManage,
    ]);
}
