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
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } = [];
}
