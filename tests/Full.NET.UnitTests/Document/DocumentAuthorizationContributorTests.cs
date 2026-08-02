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
                HostDocumentPermissions.AddVersion,
                HostDocumentPermissions.Create,
                HostDocumentPermissions.Delete,
                HostDocumentPermissions.Read,
                HostDocumentPermissions.Restore,
                HostDocumentPermissions.Update,
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

        var expectedActions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["create"] = HostDocumentPermissions.Create,
            ["update"] = HostDocumentPermissions.Update,
            ["add_version"] = HostDocumentPermissions.AddVersion,
            ["delete"] = HostDocumentPermissions.Delete,
            ["restore"] = HostDocumentPermissions.Restore,
        };
        var itemActions = catalog.Actions
            .Where(action => action.NavigationId == "host-document-items")
            .ToDictionary(
                action => action.ClientActionKey,
                action => action.PermissionCode,
                StringComparer.Ordinal);
        CollectionAssert.AreEquivalent(expectedActions, itemActions);
    }
}
