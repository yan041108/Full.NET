using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Webhooks.Contracts;

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
            "管理 Webhook 订阅",
            AuthorizationScope.Host | AuthorizationScope.Tenant),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } = [];

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } = [];
}
