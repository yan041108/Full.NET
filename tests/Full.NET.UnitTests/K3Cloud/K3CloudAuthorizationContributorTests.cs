using Full.NET.Modules.K3Cloud;
using Full.NET.Modules.K3Cloud.Contracts;
using Full.NET.Modules.Identity.Authorization;

namespace Full.NET.UnitTests.K3Cloud;

[TestClass]
public sealed class K3CloudAuthorizationContributorTests
{
    [TestMethod]
    public void Contributor_publishes_k3cloud_permissions_and_navigation()
    {
        var catalog = AuthorizationCatalog.Create([new K3CloudAuthorizationContributor()]);

        CollectionAssert.AreEquivalent(
            new[]
            {
                K3CloudConnectionPermissions.Read,
                K3CloudConnectionPermissions.Create,
                K3CloudConnectionPermissions.Update,
                K3CloudConnectionPermissions.Test,
                K3CloudDocumentSyncPermissions.Read,
                K3CloudDocumentSyncPermissions.Create,
                K3CloudDocumentSyncPermissions.Retry,
            },
            catalog.Permissions.Select(permission => permission.Code).ToArray());

        var syncs = catalog.Navigation.Single(item => item.Id == "k3cloud-document-syncs");
        Assert.AreEqual(K3CloudDocumentSyncPermissions.Read, syncs.RequiredPermission);
        Assert.AreEqual("/k3cloud/document-syncs", syncs.Path);
    }
}
