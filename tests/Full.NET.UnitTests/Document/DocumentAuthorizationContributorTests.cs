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
                HostDocumentCategoryPermissions.Create,
                HostDocumentCategoryPermissions.Delete,
                HostDocumentCategoryPermissions.Read,
                HostDocumentCategoryPermissions.Update,
                HostDocumentPermissions.AddVersion,
                HostDocumentPermissions.Create,
                HostDocumentPermissions.Delete,
                HostDocumentPermissions.Download,
                HostDocumentPermissions.Read,
                HostDocumentPermissions.Restore,
                HostDocumentPermissions.Update,
                HostDocumentTagPermissions.Create,
                HostDocumentTagPermissions.Delete,
                HostDocumentTagPermissions.Read,
                HostDocumentTagPermissions.Update,
            },
            catalog.Permissions.Select(permission => permission.Code).ToArray());

        var hostItems = catalog.Navigation.Single(item => item.Id == "host-document-items");
        Assert.AreEqual("/document/host-items", hostItems.Path);
        Assert.AreEqual(HostDocumentPermissions.Read, hostItems.RequiredPermission);

        var categories = catalog.Navigation.Single(item => item.Id == "document-categories");
        Assert.AreEqual(HostDocumentCategoryPermissions.Read, categories.RequiredPermission);

        var tags = catalog.Navigation.Single(item => item.Id == "document-tags");
        Assert.AreEqual(HostDocumentTagPermissions.Read, tags.RequiredPermission);

        var expectedItemActions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["create"] = HostDocumentPermissions.Create,
            ["update"] = HostDocumentPermissions.Update,
            ["add_version"] = HostDocumentPermissions.AddVersion,
            ["download"] = HostDocumentPermissions.Download,
            ["delete"] = HostDocumentPermissions.Delete,
            ["restore"] = HostDocumentPermissions.Restore,
        };
        var itemActions = catalog.Actions
            .Where(action => action.NavigationId == "host-document-items")
            .ToDictionary(
                action => action.ClientActionKey,
                action => action.PermissionCode,
                StringComparer.Ordinal);
        CollectionAssert.AreEquivalent(expectedItemActions, itemActions);

        var expectedCategoryActions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["create"] = HostDocumentCategoryPermissions.Create,
            ["update"] = HostDocumentCategoryPermissions.Update,
            ["delete"] = HostDocumentCategoryPermissions.Delete,
        };
        var categoryActions = catalog.Actions
            .Where(action => action.NavigationId == "document-categories")
            .ToDictionary(
                action => action.ClientActionKey,
                action => action.PermissionCode,
                StringComparer.Ordinal);
        CollectionAssert.AreEquivalent(expectedCategoryActions, categoryActions);

        var expectedTagActions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["create"] = HostDocumentTagPermissions.Create,
            ["update"] = HostDocumentTagPermissions.Update,
            ["delete"] = HostDocumentTagPermissions.Delete,
        };
        var tagActions = catalog.Actions
            .Where(action => action.NavigationId == "document-tags")
            .ToDictionary(
                action => action.ClientActionKey,
                action => action.PermissionCode,
                StringComparer.Ordinal);
        CollectionAssert.AreEquivalent(expectedTagActions, tagActions);
    }
}
