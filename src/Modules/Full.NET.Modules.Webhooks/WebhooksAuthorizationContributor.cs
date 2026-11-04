using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Webhooks.Contracts;

/// <summary>Webhooks 模块授权目录贡献者；向 Identity 注册权限、导航与精确动作。</summary>
namespace Full.NET.Modules.Webhooks;

internal sealed class WebhooksAuthorizationContributor : IAuthorizationCatalogContributor
{
    public AuthorizationModuleDefinition Module { get; } =
        new("webhooks", "Webhook", 83);

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition(
            WebhookPermissions.SubscriptionsRead,
            "读取 Webhook 订阅",
            AuthorizationScope.Host | AuthorizationScope.Tenant),
        new PermissionDefinition(
            WebhookPermissions.SubscriptionsManage,
            "创建 Webhook 订阅",
            AuthorizationScope.Host | AuthorizationScope.Tenant),
        new PermissionDefinition(WebhookPermissions.SubscriptionsSetStatus, "暂停或恢复 Webhook 订阅", AuthorizationScope.Tenant),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [new("webhook-subscriptions", null, "webhook-subscriptions", "/webhooks/subscriptions", "webhook-subscriptions",
        "Webhook 订阅", "Webhook subscriptions", "link", 10, WebhookPermissions.SubscriptionsRead)];

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [new(WebhookPermissions.SubscriptionsManage, "webhook-subscriptions", WebhookPermissions.SubscriptionsManage, "创建订阅", "create", 10),
     new(WebhookPermissions.SubscriptionsSetStatus, "webhook-subscriptions", WebhookPermissions.SubscriptionsSetStatus, "暂停或恢复订阅", "set-status", 20)];
}
