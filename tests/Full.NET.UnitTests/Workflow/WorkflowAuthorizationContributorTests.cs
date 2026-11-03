using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Workflow;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Resources;
using System.Globalization;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Features.GetNavigation;
using Full.NET.Modules.Identity.Features.ManageHostMenus;
using Full.NET.Modules.Identity.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>单元测试（工作流）：Workflow Authorization Contributor。</summary>
[TestClass]
public sealed class WorkflowAuthorizationContributorTests
{
    [TestMethod]
    [DataRow(true)][DataRow(false)]
    public async Task Generated_workflow_directory_allows_todo_only_members_without_grant_or_definition_management(bool host)
    {
        var catalog = AuthorizationCatalog.Create([new IdentityAuthorizationContributor(), new WorkflowAuthorizationContributor()]);
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
        var inserted = new List<InsertIdentityNavigation>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
        { if (call.ArgAt<object?>(1) is InsertIdentityNavigation menu) inserted.Add(menu); return 1; });
        var sync = new HostNavigationCatalogSyncService(query, command, catalog, Substitute.For<IClock>(), ids);
        await sync.SyncMissingCatalogEntriesAsync();
        var directory = inserted.Single(item => item.RouteName == "module-workflow");
        Assert.AreEqual(IdentityAuthorizationContributor.NavigationRead, directory.RequiredPermission);
        var routes = inserted.ToDictionary(item => item.Id, item => item.RouteName);
        var definitions = inserted.Where(item => item.RouteName == "module-workflow" || item.RouteName.StartsWith("workflow-", StringComparison.Ordinal))
            .Where(item => item.MenuType != "button").Select(item => new NavigationDefinition(item.RouteName,
                item.ParentId is { } parent ? routes[parent] : null, item.RouteName, item.Path, item.ComponentKey,
                item.Title, item.Caption, item.Icon, item.DisplayOrder, item.RequiredPermission, MenuType: item.MenuType)).ToArray();
        var projected = new NavigationProjector(catalog).Project([IdentityAuthorizationContributor.NavigationRead, WorkflowPermissions.TodosRead], host, definitions);
        var workflow = projected.Single(item => item.Id == "module-workflow");
        CollectionAssert.AreEqual(new[] { "workflow-todos" }, workflow.Children.Select(item => item.Id).ToArray());
        await command.Received(1).ExecuteAsync(HostNavigationCatalogSyncService.RepairWorkflowDirectoryPermission, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }
    [TestMethod]
    [DataRow("zh-CN")]
    [DataRow("en-US")]
    public void Collaboration_errors_have_localized_resources(string culture)
    {
        var resources = new WorkflowErrorResourceSource();
        foreach (var code in new[] { WorkflowErrorCodes.CollaborationGrantInvalid, WorkflowErrorCodes.CollaborationGrantNotFound, WorkflowErrorCodes.CollaborationGrantConflict })
        {
            Assert.IsTrue(resources.TryGetTemplate(code, CultureInfo.GetCultureInfo(culture), out var message));
            StringAssert.Contains(message, culture == "zh-CN" ? "协作授权" : "collaboration grant");
        }
    }
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
                WorkflowPermissions.CollaborationGrantsRead,
                WorkflowPermissions.DefinitionsRead,
                WorkflowPermissions.FormsRead,
                WorkflowPermissions.CalendarsRead,
                WorkflowPermissions.InstancesRead,
                WorkflowPermissions.TodosRead,
                WorkflowPermissions.ClaimPoolsRead,
                WorkflowPermissions.CcRead,
                WorkflowPermissions.RecoveryTasksRead,
            },
            catalog.Navigation.Select(item => item.RequiredPermission).ToArray());
        CollectionAssert.AreEquivalent(
            WorkflowPermissions.All.Except(
            [
                WorkflowPermissions.CollaborationGrantsRead,
                WorkflowPermissions.DefinitionsRead,
                WorkflowPermissions.FormsRead,
                WorkflowPermissions.CalendarsRead,
                WorkflowPermissions.InstancesRead,
                WorkflowPermissions.TodosRead,
                WorkflowPermissions.ClaimPoolsRead,
                WorkflowPermissions.CcRead,
                WorkflowPermissions.RecoveryTasksRead,
            ]).ToArray(),
            catalog.Actions.Select(item => item.PermissionCode).ToArray());
    }
}
