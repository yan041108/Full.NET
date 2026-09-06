using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class IdentityAuthorizationContributorTests
{
    [TestMethod]
    public void Contributor_publishes_registration_permissions_and_navigation()
    {
        var catalog = AuthorizationCatalog.Create([new IdentityAuthorizationContributor()]);

        CollectionAssert.Contains(
            catalog.Permissions.Select(permission => permission.Code).ToArray(),
            IdentityRegistrationPolicyPermissions.Read);
        CollectionAssert.Contains(
            catalog.Permissions.Select(permission => permission.Code).ToArray(),
            IdentityRegistrationPolicyPermissions.Update);
        CollectionAssert.Contains(
            catalog.Permissions.Select(permission => permission.Code).ToArray(),
            IdentityRegistrationWayPermissions.Read);
        CollectionAssert.Contains(
            catalog.Permissions.Select(permission => permission.Code).ToArray(),
            IdentityRegistrationWayPermissions.Create);
        CollectionAssert.Contains(
            catalog.Permissions.Select(permission => permission.Code).ToArray(),
            IdentityRegistrationWayPermissions.Update);
        CollectionAssert.Contains(
            catalog.Permissions.Select(permission => permission.Code).ToArray(),
            IdentityRegistrationWayPermissions.Delete);

        var registrationWays = catalog.Navigation.Single(item => item.Id == "registration-ways");
        Assert.AreEqual("/identity/registration-ways", registrationWays.Path);
        Assert.AreEqual(IdentityRegistrationWayPermissions.Read, registrationWays.RequiredPermission);

        CollectionAssert.AreEquivalent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["create"] = IdentityRegistrationWayPermissions.Create,
                ["update"] = IdentityRegistrationWayPermissions.Update,
                ["delete"] = IdentityRegistrationWayPermissions.Delete,
            },
            catalog.Actions
                .Where(action => action.NavigationId == "registration-ways")
                .ToDictionary(
                    action => action.ClientActionKey,
                    action => action.PermissionCode,
                    StringComparer.Ordinal));
    }
}
