using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Document;

internal sealed class DocumentAuthorizationContributor : IAuthorizationCatalogContributor
{
    public AuthorizationModuleDefinition Module { get; } =
        new("document", "文档中心", 100);

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new(HostDocumentPermissions.Read, "读取 Host 文档库", AuthorizationScope.Host),
        new(HostDocumentPermissions.Create, "创建 Host 文档", AuthorizationScope.Host),
        new(HostDocumentPermissions.Update, "更新 Host 文档元数据", AuthorizationScope.Host),
        new(HostDocumentPermissions.AddVersion, "上传 Host 文档新版本", AuthorizationScope.Host),
        new(HostDocumentPermissions.Delete, "删除 Host 文档", AuthorizationScope.Host),
        new(HostDocumentPermissions.Restore, "恢复 Host 文档", AuthorizationScope.Host),
        new(HostDocumentCategoryPermissions.Manage, "维护 Host 文档分类", AuthorizationScope.Host),
        new(HostDocumentTagPermissions.Manage, "维护 Host 文档标签", AuthorizationScope.Host),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "host-document-items",
            null,
            "host-document-items",
            "/document/host-items",
            "host-document-items",
            "Host 文档库",
            "Host Documents",
            "document",
            71,
            HostDocumentPermissions.Read),
        new NavigationDefinition(
            "document-categories",
            null,
            "document-categories",
            "/document/categories",
            "document-categories",
            "文档分类",
            "Document Categories",
            "collection",
            72,
            HostDocumentCategoryPermissions.Manage),
        new NavigationDefinition(
            "document-tags",
            null,
            "document-tags",
            "/document/tags",
            "document-tags",
            "文档标签",
            "Document Tags",
            "price-tag",
            73,
            HostDocumentTagPermissions.Manage),
    ];

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "document.host_documents.create",
            "host-document-items",
            HostDocumentPermissions.Create,
            "创建文档",
            "create",
            10),
        new AuthorizationActionDefinition(
            "document.host_documents.update",
            "host-document-items",
            HostDocumentPermissions.Update,
            "编辑文档",
            "update",
            20),
        new AuthorizationActionDefinition(
            "document.host_documents.add_version",
            "host-document-items",
            HostDocumentPermissions.AddVersion,
            "上传新版本",
            "add_version",
            30),
        new AuthorizationActionDefinition(
            "document.host_documents.delete",
            "host-document-items",
            HostDocumentPermissions.Delete,
            "删除文档",
            "delete",
            40),
        new AuthorizationActionDefinition(
            "document.host_documents.restore",
            "host-document-items",
            HostDocumentPermissions.Restore,
            "恢复文档",
            "restore",
            50),
    ];
}
