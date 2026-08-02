using Full.NET.Modules.Document;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.UnitTests.Document;

[TestClass]
public sealed class DocumentAuthorizationContributorTests
{
    [TestMethod]
    public void Contributor_publishes_document_permissions_and_navigation()
    {
        var catalog = AuthorizationCatalog.Create([new DocumentAuthorizationContributor()]);

        CollectionAssert.AreEqual(
            new[]
            {
                HostDocumentCategoryPermissions.Manage,
                HostDocumentPermissions.Delete,
                HostDocumentPermissions.Read,
                HostDocumentPermissions.Write,
                HostDocumentTagPermissions.Manage,
            },
            catalog.Permissions.Select(permission => permission.Code).ToArray());

        var hostItems = catalog.Navigation.Single(item => item.Id == "host-document-items");
        Assert.AreEqual("/document/host-items", hostItems.Path);
        Assert.AreEqual(HostDocumentPermissions.Read, hostItems.RequiredPermission);

        var categories = catalog.Navigation.Single(item => item.Id == "document-categories");
        Assert.AreEqual(HostDocumentCategoryPermissions.Manage, categories.RequiredPermission);

        var tags = catalog.Navigation.Single(item => item.Id == "document-tags");
        Assert.AreEqual(HostDocumentTagPermissions.Manage, tags.RequiredPermission);
    }
}
