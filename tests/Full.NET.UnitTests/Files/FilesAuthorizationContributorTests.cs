using Full.NET.Modules.Files;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.UnitTests.Files;

[TestClass]
public sealed class FilesAuthorizationContributorTests
{
    [TestMethod]
    public void Contributor_publishes_exact_host_file_permissions_and_actions()
    {
        var catalog = AuthorizationCatalog.Create([new FilesAuthorizationContributor()]);

        CollectionAssert.AreEqual(
            new[]
            {
                HostFilePermissions.ReferencesRead,
                HostFilePermissions.Delete,
                HostFilePermissions.Download,
                HostFilePermissions.Read,
                HostFilePermissions.Update,
                HostFilePermissions.Upload,
                HostFolderPermissions.Create,
                HostFolderPermissions.Delete,
                HostFolderPermissions.Update,
                StorageProviderPermissions.Read,
                StorageProviderPermissions.Test,
            },
            catalog.Permissions.Select(permission => permission.Code).ToArray());

        var hostFiles = catalog.Navigation.Single(item => item.Id == "host-files");
        Assert.AreEqual(HostFilePermissions.Read, hostFiles.RequiredPermission);

        var storageProviders = catalog.Navigation.Single(item => item.Id == "storage-providers");
        Assert.AreEqual(StorageProviderPermissions.Read, storageProviders.RequiredPermission);

        CollectionAssert.AreEqual(
            new[]
            {
                HostFilePermissions.Upload,
                HostFilePermissions.Download,
                HostFilePermissions.Update,
                HostFilePermissions.ReferencesRead,
                HostFilePermissions.Delete,
                HostFolderPermissions.Create,
                HostFolderPermissions.Update,
                HostFolderPermissions.Delete,
            },
            catalog.Actions
                .Where(action => action.NavigationId == "host-files")
                .OrderBy(action => action.Order)
                .Select(action => action.PermissionCode)
                .ToArray());

        CollectionAssert.AreEqual(
            new[] { StorageProviderPermissions.Test },
            catalog.Actions
                .Where(action => action.NavigationId == "storage-providers")
                .OrderBy(action => action.Order)
                .Select(action => action.PermissionCode)
                .ToArray());
    }
}
