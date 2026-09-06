using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Reporting;
using Full.NET.Modules.Reporting.Contracts;

namespace Full.NET.UnitTests.Reporting;

[TestClass]
public sealed class ReportingAuthorizationContributorTests
{
    [TestMethod]
    public void Contributor_publishes_reporting_permissions_and_navigation()
    {
        var catalog = AuthorizationCatalog.Create([new ReportingAuthorizationContributor()]);

        CollectionAssert.AreEquivalent(
            new[]
            {
                ReportingDataSourcePermissions.Read,
                ReportingDataSourcePermissions.Create,
                ReportingDataSourcePermissions.Update,
                ReportingDataSourcePermissions.Delete,
                ReportingDataSourcePermissions.Test,
                ReportingGroupPermissions.Read,
                ReportingGroupPermissions.Create,
                ReportingGroupPermissions.Update,
                ReportingGroupPermissions.Delete,
                ReportingDefinitionPermissions.Read,
                ReportingDefinitionPermissions.Create,
                ReportingDefinitionPermissions.Update,
                ReportingDefinitionPermissions.Delete,
                ReportingDefinitionPermissions.Publish,
                ReportingQueryPortPermissions.Read,
                ReportingExecutionPermissions.Run,
                ReportingExecutionPermissions.ColumnSchemaName,
            },
            catalog.Permissions.Select(permission => permission.Code).ToArray());

        var dataSources = catalog.Navigation.Single(item => item.Id == "reporting-data-sources");
        Assert.AreEqual(ReportingDataSourcePermissions.Read, dataSources.RequiredPermission);
        Assert.AreEqual("/reporting/data-sources", dataSources.Path);

        var definitions = catalog.Navigation.Single(item => item.Id == "reporting-definitions");
        Assert.AreEqual(ReportingDefinitionPermissions.Read, definitions.RequiredPermission);
        Assert.AreEqual("/reporting/definitions", definitions.Path);

        var execute = catalog.Navigation.Single(item => item.Id == "reporting-execute");
        Assert.AreEqual(ReportingExecutionPermissions.Run, execute.RequiredPermission);
        Assert.AreEqual("/reporting/execute", execute.Path);

        CollectionAssert.AreEquivalent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["create"] = ReportingDataSourcePermissions.Create,
                ["update"] = ReportingDataSourcePermissions.Update,
                ["delete"] = ReportingDataSourcePermissions.Delete,
                ["test"] = ReportingDataSourcePermissions.Test,
            },
            catalog.Actions
                .Where(action => action.NavigationId == "reporting-data-sources")
                .ToDictionary(
                    action => action.ClientActionKey,
                    action => action.PermissionCode,
                    StringComparer.Ordinal));

        CollectionAssert.AreEquivalent(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["create-group"] = ReportingGroupPermissions.Create,
                ["update-group"] = ReportingGroupPermissions.Update,
                ["delete-group"] = ReportingGroupPermissions.Delete,
                ["create-definition"] = ReportingDefinitionPermissions.Create,
                ["update-definition"] = ReportingDefinitionPermissions.Update,
                ["delete-definition"] = ReportingDefinitionPermissions.Delete,
                ["publish"] = ReportingDefinitionPermissions.Publish,
            },
            catalog.Actions
                .Where(action => action.NavigationId == "reporting-definitions")
                .ToDictionary(
                    action => action.ClientActionKey,
                    action => action.PermissionCode,
                    StringComparer.Ordinal));
    }
}
