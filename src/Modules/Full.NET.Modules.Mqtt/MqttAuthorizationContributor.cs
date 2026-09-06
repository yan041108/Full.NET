using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Mqtt.Contracts;

namespace Full.NET.Modules.Mqtt;

/// <summary>MQTT 模块授权目录贡献者。</summary>
internal sealed class MqttAuthorizationContributor : IAuthorizationCatalogContributor
{
    /// <inheritdoc />
    public AuthorizationModuleDefinition Module { get; } =
        new("mqtt", "MQTT", 82);

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition(
            MqttPermissions.BrokerRead,
            "读取 MQTT Broker 部署状态",
            AuthorizationScope.Host),
        new PermissionDefinition(
            MqttPermissions.ClientsRead,
            "读取 MQTT 客户端目录",
            AuthorizationScope.Host),
        new PermissionDefinition(
            MqttPermissions.MessagesRead,
            "读取 MQTT 消息记录",
            AuthorizationScope.Host | AuthorizationScope.Tenant),
        new PermissionDefinition(
            MqttPermissions.MessagesPublish,
            "在 ACL 与限额内发布 MQTT 消息",
            AuthorizationScope.Host | AuthorizationScope.Tenant),
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "mqtt-control-plane",
            null,
            "mqtt-control-plane",
            "/mqtt/control-plane",
            "mqtt-control-plane",
            "MQTT 控制面",
            "MQTT Control Plane",
            "connection",
            75,
            MqttPermissions.BrokerRead),
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "mqtt.messages.publish",
            "mqtt-control-plane",
            MqttPermissions.MessagesPublish,
            "发布消息",
            "publish",
            10),
    ];
}
