using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.K3Cloud.Contracts;

namespace Full.NET.Modules.K3Cloud;

/// <summary>向 Identity 授权目录注册 K3Cloud 权限、导航与页面操作。</summary>
internal sealed class K3CloudAuthorizationContributor : IAuthorizationCatalogContributor
{
    public AuthorizationModuleDefinition Module { get; } =
        new("k3cloud", "K3Cloud", 121);

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new(K3CloudConnectionPermissions.Read, "读取 K3Cloud 连接配置", AuthorizationScope.Host),
        new(K3CloudConnectionPermissions.Create, "创建 K3Cloud 连接配置", AuthorizationScope.Host),
        new(K3CloudConnectionPermissions.Update, "更新 K3Cloud 连接配置", AuthorizationScope.Host),
        new(K3CloudConnectionPermissions.Test, "测试 K3Cloud 连接", AuthorizationScope.Host),
        new(K3CloudDocumentSyncPermissions.Read, "读取 K3Cloud 单据同步", AuthorizationScope.Host),
        new(K3CloudDocumentSyncPermissions.Create, "创建 K3Cloud 单据同步", AuthorizationScope.Host),
        new(K3CloudDocumentSyncPermissions.Retry, "重试 K3Cloud 单据同步", AuthorizationScope.Host),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "k3cloud-connection-configs",
            null,
            "k3cloud-connection-configs",
            "/k3cloud/connection-configs",
            "k3cloud-connection-configs",
            "K3Cloud 连接",
            "K3Cloud Connections",
            "connection",
            10,
            K3CloudConnectionPermissions.Read),
        new NavigationDefinition(
            "k3cloud-document-syncs",
            null,
            "k3cloud-document-syncs",
            "/k3cloud/document-syncs",
            "k3cloud-document-syncs",
            "K3Cloud 同步",
            "K3Cloud Sync",
            "refresh",
            20,
            K3CloudDocumentSyncPermissions.Read),
    ];

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "k3cloud.connections.create",
            "k3cloud-connection-configs",
            K3CloudConnectionPermissions.Create,
            "新建连接",
            "create",
            10),
        new AuthorizationActionDefinition(
            "k3cloud.connections.update",
            "k3cloud-connection-configs",
            K3CloudConnectionPermissions.Update,
            "编辑连接",
            "update",
            20),
        new AuthorizationActionDefinition(
            "k3cloud.connections.test",
            "k3cloud-connection-configs",
            K3CloudConnectionPermissions.Test,
            "测试连接",
            "test",
            30),
        new AuthorizationActionDefinition(
            "k3cloud.document_syncs.create",
            "k3cloud-document-syncs",
            K3CloudDocumentSyncPermissions.Create,
            "创建同步",
            "create",
            10),
        new AuthorizationActionDefinition(
            "k3cloud.document_syncs.retry",
            "k3cloud-document-syncs",
            K3CloudDocumentSyncPermissions.Retry,
            "重试同步",
            "retry",
            20),
    ];
}
