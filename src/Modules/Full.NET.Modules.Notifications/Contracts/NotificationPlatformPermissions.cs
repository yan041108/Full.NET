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
    public const string TemplatesRead = "notifications.templates.read";
    public const string TemplatesCreate = "notifications.templates.create";
    public const string TemplatesUpdate = "notifications.templates.update";
    public const string TemplatesPublish = "notifications.templates.publish";
    public const string IntentsRead = "notifications.intents.read";
    public const string IntentsCreate = "notifications.intents.create";
    public const string ProviderProfilesRead = "notifications.provider_profiles.read";
    public const string ProviderProfilesCreate = "notifications.provider_profiles.create";
    public const string ProviderProfilesUpdate = "notifications.provider_profiles.update";
    public const string ProviderProfilesPublish = "notifications.provider_profiles.publish";
    public const string ProviderProfilesEnable = "notifications.provider_profiles.enable";
    public const string ProviderProfilesDisable = "notifications.provider_profiles.disable";
    public const string BindingsRead = "notifications.bindings.read";
    public const string BindingsCreate = "notifications.bindings.create";
    public const string BindingsUpdate = "notifications.bindings.update";
    public const string BindingsPublish = "notifications.bindings.publish";
    public const string DeliveriesRead = "notifications.deliveries.read";
    public const string DeliveriesRetry = "notifications.deliveries.retry";
    public const string DeliveriesDeadLetter = "notifications.deliveries.dead_letter";
    public const string PreferencesRead = "notifications.preferences.read";
    public const string PreferencesUpdate = "notifications.preferences.update";
    public const string PreferencesManage = "notifications.preferences.manage";

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
