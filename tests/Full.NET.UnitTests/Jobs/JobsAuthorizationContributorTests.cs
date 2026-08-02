using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Jobs;
using Full.NET.Modules.Jobs.Contracts;

namespace Full.NET.UnitTests.Jobs;

[TestClass]
public sealed class JobsAuthorizationContributorTests
{
    [TestMethod]
    public void Contributor_publishes_exact_host_job_permissions_actions_and_navigation()
    {
        var catalog = AuthorizationCatalog.Create([new JobsAuthorizationContributor()]);

        CollectionAssert.AreEqual(
            new[]
            {
                HostJobPermissions.DefinitionsCreate,
                HostJobPermissions.DefinitionsDisable,
                HostJobPermissions.DefinitionsRead,
                HostJobPermissions.DefinitionsTrigger,
                HostJobPermissions.DefinitionsUpdate,
                HostJobPermissions.ExecutionsRead,
                HostJobPermissions.SchedulesCreate,
                HostJobPermissions.SchedulesPause,
                HostJobPermissions.SchedulesRead,
                HostJobPermissions.SchedulesResume,
                HostJobPermissions.SchedulesUpdate,
            },
            catalog.Permissions.Select(permission => permission.Code).ToArray());

        var hostJobs = catalog.Navigation.Single(item => item.Id == "host-jobs");
        Assert.AreEqual(HostJobPermissions.DefinitionsRead, hostJobs.RequiredPermission);
        var hostSchedules = catalog.Navigation.Single(item => item.Id == "host-job-schedules");
        Assert.AreEqual(HostJobPermissions.SchedulesRead, hostSchedules.RequiredPermission);

        CollectionAssert.AreEqual(
            new[]
            {
                HostJobPermissions.DefinitionsCreate,
                HostJobPermissions.DefinitionsUpdate,
                HostJobPermissions.DefinitionsDisable,
                HostJobPermissions.DefinitionsTrigger,
            },
            catalog.Actions
                .Where(action => action.NavigationId == "host-jobs")
                .OrderBy(action => action.Order)
                .Select(action => action.PermissionCode)
                .ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                HostJobPermissions.SchedulesCreate,
                HostJobPermissions.SchedulesUpdate,
                HostJobPermissions.SchedulesPause,
                HostJobPermissions.SchedulesResume,
            },
            catalog.Actions
                .Where(action => action.NavigationId == "host-job-schedules")
                .OrderBy(action => action.Order)
                .Select(action => action.PermissionCode)
                .ToArray());
    }
}
