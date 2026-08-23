using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Messaging.Contracts;

namespace Full.NET.Modules.Messaging;

/// <summary>
/// 向授权目录贡献 Messaging 运维模块的权限、导航与操作定义。
/// </summary>
/// <remarks>
/// 交付所有权切换/回退与死信/Kafka 范围重放均为高风险运维操作，各自绑定独立稳定权限码；
/// 客户端可见性仅负责体验，所有运维 Endpoint 仍按精确权限重新校验，避免越权触发切流或重放。
/// </remarks>
internal sealed class MessagingAuthorizationContributor : IAuthorizationCatalogContributor
{
    public AuthorizationModuleDefinition Module { get; } =
        new("messaging", "消息运维", 75);

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition(
            MessagingPermissions.EventsRead,
            "查询事件交付状态",
            AuthorizationScope.Host),
        new PermissionDefinition(
            MessagingPermissions.DeadLettersRead,
            "查询消费死信",
            AuthorizationScope.Host),
        new PermissionDefinition(
            MessagingPermissions.DeadLettersReplay,
            "重放消费死信",
            AuthorizationScope.Host),
        new PermissionDefinition(
            MessagingPermissions.KafkaRangeReplay,
            "按范围重放 Kafka 消息",
            AuthorizationScope.Host),
        new PermissionDefinition(
            MessagingPermissions.DeliveryCutover,
            "切换事件交付所有权",
            AuthorizationScope.Host),
        new PermissionDefinition(
            MessagingPermissions.DeliveryRollback,
            "回退事件交付所有权",
            AuthorizationScope.Host),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "host-messaging-ops",
            null,
            "host-messaging-ops",
            "/messaging/operations",
            "host-messaging-ops",
            "消息运维",
            "Messaging",
            "connection",
            59,
            MessagingPermissions.EventsRead),
    ];

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "messaging.dead_letters.replay",
            "host-messaging-ops",
            MessagingPermissions.DeadLettersReplay,
            "重放死信",
            "replay",
            10),
        new AuthorizationActionDefinition(
            "messaging.kafka.range_replay",
            "host-messaging-ops",
            MessagingPermissions.KafkaRangeReplay,
            "范围重放",
            "range-replay",
            15),
        new AuthorizationActionDefinition(
            "messaging.delivery.cutover",
            "host-messaging-ops",
            MessagingPermissions.DeliveryCutover,
            "切换交付所有权",
            "cutover",
            20),
        new AuthorizationActionDefinition(
            "messaging.delivery.rollback",
            "host-messaging-ops",
            MessagingPermissions.DeliveryRollback,
            "回退交付所有权",
            "rollback",
            30),
    ];
}
