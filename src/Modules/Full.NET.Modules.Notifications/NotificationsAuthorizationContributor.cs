using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.Modules.Notifications;

/// <summary>
/// 向授权目录贡献 Notifications 模块的权限、导航与操作定义。
/// </summary>
/// <remarks>
/// 公告操作仍归属 Host 作用域；站内信读写在 Host 与 Tenant 会话中共用权限码，发送路径由服务按受信作用域关闭。
/// 模板、Profile、Binding、投递与偏好同时支持 Host 与 Tenant。
/// 每个受保护操作绑定独立稳定权限码，客户端可见性仅负责体验，服务端 Endpoint 仍按精确权限重新校验。
/// </remarks>
internal sealed class NotificationsAuthorizationContributor
    : IAuthorizationCatalogContributor
{
    private const AuthorizationScope PlatformScopes =
        AuthorizationScope.Host | AuthorizationScope.Tenant;

    public AuthorizationModuleDefinition Module { get; } =
        new("notifications", "通知中心", 60);

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition(
            HostAnnouncementPermissions.Read,
            "查询公告",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostAnnouncementPermissions.Create,
            "创建公告",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostAnnouncementPermissions.Update,
            "更新公告",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostAnnouncementPermissions.Publish,
            "发布公告",
            AuthorizationScope.Host),
        new PermissionDefinition(
            HostAnnouncementPermissions.Retract,
            "撤回公告",
            AuthorizationScope.Host),
        new PermissionDefinition(
            InboxPermissions.Read,
            "查询站内信",
            PlatformScopes),
        new PermissionDefinition(
            InboxPermissions.Send,
            "发送站内信",
            PlatformScopes),
        new PermissionDefinition(
            InboxPermissions.MarkRead,
            "标记站内信已读",
            PlatformScopes),
        new PermissionDefinition(
            InboxPermissions.MarkAllRead,
            "全部标记站内信已读",
            PlatformScopes),
        PlatformPermission(NotificationPlatformPermissions.TemplatesRead, "查询通知模板"),
        PlatformPermission(NotificationPlatformPermissions.TemplatesCreate, "新建通知模板"),
        PlatformPermission(NotificationPlatformPermissions.TemplatesUpdate, "编辑通知模板草稿"),
        PlatformPermission(NotificationPlatformPermissions.TemplatesPublish, "发布通知模板版本"),
        PlatformPermission(NotificationPlatformPermissions.IntentsRead, "查询通知意图"),
        PlatformPermission(NotificationPlatformPermissions.IntentsCreate, "创建通知意图"),
        PlatformPermission(NotificationPlatformPermissions.ProviderProfilesRead, "查询渠道配置"),
        PlatformPermission(NotificationPlatformPermissions.ProviderProfilesCreate, "新建渠道配置"),
        PlatformPermission(NotificationPlatformPermissions.ProviderProfilesUpdate, "编辑渠道非密钥配置"),
        PlatformPermission(NotificationPlatformPermissions.ProviderProfilesPublish, "发布渠道配置版本"),
        PlatformPermission(NotificationPlatformPermissions.ProviderProfilesEnable, "启用渠道配置"),
        PlatformPermission(NotificationPlatformPermissions.ProviderProfilesDisable, "停用渠道配置"),
        PlatformPermission(NotificationPlatformPermissions.BindingsRead, "查询场景绑定"),
        PlatformPermission(NotificationPlatformPermissions.BindingsCreate, "新建场景绑定"),
        PlatformPermission(NotificationPlatformPermissions.BindingsUpdate, "编辑场景绑定草稿"),
        PlatformPermission(NotificationPlatformPermissions.BindingsPublish, "发布场景绑定版本"),
        PlatformPermission(NotificationPlatformPermissions.DeliveriesRead, "查询投递与尝试"),
        PlatformPermission(NotificationPlatformPermissions.DeliveriesRetry, "人工重试投递"),
        PlatformPermission(NotificationPlatformPermissions.DeliveriesDeadLetter, "处置投递死信"),
        PlatformPermission(NotificationPlatformPermissions.PreferencesRead, "查询本人通知偏好"),
        PlatformPermission(NotificationPlatformPermissions.PreferencesUpdate, "更新本人通知偏好"),
        PlatformPermission(NotificationPlatformPermissions.PreferencesManage, "代管用户通知偏好"),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "host-announcements",
            null,
            "host-announcements",
            "/notifications/host-announcements",
            "host-announcements",
            "公告管理",
            "Announcements",
            "bell",
            55,
            HostAnnouncementPermissions.Read),
        new NavigationDefinition(
            "inbox-messages",
            null,
            "inbox-messages",
            "/notifications/inbox-messages",
            "inbox-messages",
            "消息中心",
            "Inbox",
            "message",
            56,
            InboxPermissions.Read),
        PlatformNavigation("notification-templates", "/notifications/templates", "通知模板", "Templates", 57, NotificationPlatformPermissions.TemplatesRead),
        PlatformNavigation("notification-provider-profiles", "/notifications/provider-profiles", "渠道配置", "ProviderProfiles", 58, NotificationPlatformPermissions.ProviderProfilesRead),
        PlatformNavigation("notification-bindings", "/notifications/bindings", "场景绑定", "Bindings", 59, NotificationPlatformPermissions.BindingsRead),
        PlatformNavigation("notification-deliveries", "/notifications/deliveries", "投递运维", "Deliveries", 60, NotificationPlatformPermissions.DeliveriesRead),
        PlatformNavigation("notification-preferences", "/notifications/preferences", "通知偏好", "Preferences", 61, NotificationPlatformPermissions.PreferencesRead),
    ];

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "notifications.announcements.create",
            "host-announcements",
            HostAnnouncementPermissions.Create,
            "创建公告",
            "create",
            10),
        new AuthorizationActionDefinition(
            "notifications.announcements.update",
            "host-announcements",
            HostAnnouncementPermissions.Update,
            "编辑公告",
            "update",
            20),
        new AuthorizationActionDefinition(
            "notifications.announcements.publish",
            "host-announcements",
            HostAnnouncementPermissions.Publish,
            "发布公告",
            "publish",
            30),
        new AuthorizationActionDefinition(
            "notifications.announcements.retract",
            "host-announcements",
            HostAnnouncementPermissions.Retract,
            "撤回公告",
            "retract",
            40),
        new AuthorizationActionDefinition(
            "notifications.inbox.send",
            "inbox-messages",
            InboxPermissions.Send,
            "发送站内信",
            "send",
            10),
        new AuthorizationActionDefinition(
            "notifications.inbox.mark_read",
            "inbox-messages",
            InboxPermissions.MarkRead,
            "标记已读",
            "mark_read",
            20),
        new AuthorizationActionDefinition(
            "notifications.inbox.mark_all_read",
            "inbox-messages",
            InboxPermissions.MarkAllRead,
            "全部标记已读",
            "mark_all_read",
            30),
        PlatformAction("notifications.templates.create", "notification-templates", NotificationPlatformPermissions.TemplatesCreate, "新建模板", "create", 10),
        PlatformAction("notifications.templates.update", "notification-templates", NotificationPlatformPermissions.TemplatesUpdate, "编辑草稿", "update", 20),
        PlatformAction("notifications.templates.publish", "notification-templates", NotificationPlatformPermissions.TemplatesPublish, "发布版本", "publish", 30),
        PlatformAction("notifications.intents.create", "notification-templates", NotificationPlatformPermissions.IntentsCreate, "创建通知意图", "create_intent", 40),
        PlatformAction("notifications.provider_profiles.create", "notification-provider-profiles", NotificationPlatformPermissions.ProviderProfilesCreate, "新建配置", "create", 10),
        PlatformAction("notifications.provider_profiles.update", "notification-provider-profiles", NotificationPlatformPermissions.ProviderProfilesUpdate, "编辑配置", "update", 20),
        PlatformAction("notifications.provider_profiles.publish", "notification-provider-profiles", NotificationPlatformPermissions.ProviderProfilesPublish, "发布配置", "publish", 30),
        PlatformAction("notifications.provider_profiles.enable", "notification-provider-profiles", NotificationPlatformPermissions.ProviderProfilesEnable, "启用配置", "enable", 40),
        PlatformAction("notifications.provider_profiles.disable", "notification-provider-profiles", NotificationPlatformPermissions.ProviderProfilesDisable, "停用配置", "disable", 50),
        PlatformAction("notifications.bindings.create", "notification-bindings", NotificationPlatformPermissions.BindingsCreate, "新建绑定", "create", 10),
        PlatformAction("notifications.bindings.update", "notification-bindings", NotificationPlatformPermissions.BindingsUpdate, "编辑草稿", "update", 20),
        PlatformAction("notifications.bindings.publish", "notification-bindings", NotificationPlatformPermissions.BindingsPublish, "发布绑定", "publish", 30),
        PlatformAction("notifications.deliveries.retry", "notification-deliveries", NotificationPlatformPermissions.DeliveriesRetry, "人工重试", "retry", 10),
        PlatformAction("notifications.deliveries.dead_letter", "notification-deliveries", NotificationPlatformPermissions.DeliveriesDeadLetter, "死信处置", "dead_letter", 20),
        PlatformAction("notifications.preferences.update", "notification-preferences", NotificationPlatformPermissions.PreferencesUpdate, "更新偏好", "update", 10),
        PlatformAction("notifications.preferences.manage", "notification-preferences", NotificationPlatformPermissions.PreferencesManage, "代管偏好", "manage", 20),
    ];

    private static PermissionDefinition PlatformPermission(string code, string name) =>
        new(code, name, PlatformScopes);

    private static NavigationDefinition PlatformNavigation(
        string id,
        string path,
        string title,
        string titleEn,
        int order,
        string permission) =>
        new(id, null, id, path, id, title, titleEn, "bell", order, permission);

    private static AuthorizationActionDefinition PlatformAction(
        string id,
        string navigationId,
        string permission,
        string name,
        string actionKey,
        int order) =>
        new(id, navigationId, permission, name, actionKey, order);
}
