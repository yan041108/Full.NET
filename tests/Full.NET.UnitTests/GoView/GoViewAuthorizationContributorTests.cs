using Full.NET.Modules.GoView;
using Full.NET.Modules.GoView.Contracts;
using Full.NET.Modules.Identity.Authorization;

namespace Full.NET.UnitTests.GoView;

[TestClass]
public sealed class GoViewAuthorizationContributorTests
{
    [TestMethod]
    public void Contributor_publishes_goview_permissions_and_navigation()
    {
        var catalog = AuthorizationCatalog.Create([new GoViewAuthorizationContributor()]);

        CollectionAssert.AreEquivalent(
            new[]
            {
                GoViewProjectPermissions.Read,
                GoViewProjectPermissions.Create,
                GoViewProjectPermissions.Update,
                GoViewProjectPermissions.Publish,
                GoViewProjectPermissions.Preview,
            },
            catalog.Permissions.Select(permission => permission.Code).ToArray());

        var projects = catalog.Navigation.Single(item => item.Id == "goview-projects");
        Assert.AreEqual(GoViewProjectPermissions.Read, projects.RequiredPermission);
        Assert.AreEqual("/goview/projects", projects.Path);
    }
}
