using Full.NET.Modules.ImportExport;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.Identity.Authorization;

namespace Full.NET.UnitTests.ImportExport;

[TestClass]
public sealed class ImportExportAuthorizationContributorTests
{
    [TestMethod]
    public void Contributor_publishes_import_export_permissions_and_navigation()
    {
        var catalog = AuthorizationCatalog.Create([new ImportExportAuthorizationContributor()]);

        CollectionAssert.AreEqual(
            new[]
            {
                ImportExportPermissions.ImportTasksCreate,
                ImportExportPermissions.ImportTasksExecute,
                ImportExportPermissions.ImportTasksRead,
                ImportExportPermissions.StaticSchemasRead,
            },
            catalog.Permissions.Select(permission => permission.Code).ToArray());

        var tasks = catalog.Navigation.Single(item => item.Id == "import-export-tasks");
        Assert.AreEqual("/import-export/tasks", tasks.Path);
        Assert.AreEqual(ImportExportPermissions.ImportTasksRead, tasks.RequiredPermission);

        CollectionAssert.AreEquivalent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["create"] = ImportExportPermissions.ImportTasksCreate,
                ["execute"] = ImportExportPermissions.ImportTasksExecute,
            },
            catalog.Actions
                .Where(action => action.NavigationId == "import-export-tasks")
                .ToDictionary(
                    action => action.ClientActionKey,
                    action => action.PermissionCode,
                    StringComparer.Ordinal));
    }
}
