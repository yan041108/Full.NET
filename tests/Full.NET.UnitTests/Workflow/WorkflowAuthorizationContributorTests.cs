using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Workflow;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowAuthorizationContributorTests
{
    [TestMethod]
    public void Contributor_publishes_every_spec_permission_with_host_and_tenant_scope()
    {
        var contributor = new WorkflowAuthorizationContributor();

        CollectionAssert.AreEqual(
            WorkflowPermissions.All.Order(StringComparer.Ordinal).ToArray(),
            contributor.Permissions.Select(permission => permission.Code)
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.IsTrue(contributor.Permissions.All(permission =>
            permission.Scope == (AuthorizationScope.Host | AuthorizationScope.Tenant)));
    }

    [TestMethod]
    public void Contributor_binds_pages_and_actions_to_exact_permissions()
    {
        var contributor = new WorkflowAuthorizationContributor();
        var catalog = AuthorizationCatalog.Create([contributor]);

        CollectionAssert.AreEquivalent(
            new[]
            {
                WorkflowPermissions.DefinitionsRead,
                WorkflowPermissions.FormsRead,
                WorkflowPermissions.InstancesRead,
                WorkflowPermissions.TodosRead,
                WorkflowPermissions.CcRead,
                WorkflowPermissions.RecoveryTasksRead,
            },
            catalog.Navigation.Select(item => item.RequiredPermission).ToArray());
        CollectionAssert.AreEquivalent(
            WorkflowPermissions.All.Except(
            [
                WorkflowPermissions.DefinitionsRead,
                WorkflowPermissions.FormsRead,
                WorkflowPermissions.InstancesRead,
                WorkflowPermissions.TodosRead,
                WorkflowPermissions.CcRead,
                WorkflowPermissions.RecoveryTasksRead,
            ]).ToArray(),
            catalog.Actions.Select(item => item.PermissionCode).ToArray());
    }
}
