using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Document;

internal sealed class DocumentAuthorizationContributor : IAuthorizationCatalogContributor
{
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new(HostDocumentPermissions.Read, "读取 Host 文档库", AuthorizationScope.Host),
        new(HostDocumentPermissions.Write, "维护 Host 文档库", AuthorizationScope.Host),
        new(HostDocumentPermissions.Delete, "删除或恢复 Host 文档", AuthorizationScope.Host),
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
}
