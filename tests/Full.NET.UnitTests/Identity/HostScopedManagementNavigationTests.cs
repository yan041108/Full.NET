using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.GetNavigation;
using Full.NET.Modules.Platform;
using Full.NET.Modules.Platform.Contracts;
using Full.NET.Modules.Regions;
using Full.NET.Modules.Regions.Contracts;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class HostScopedManagementNavigationTests
{
    private static readonly string[] TenantReaderPermissions =
    [
        PlatformPermissions.Read,
        RegionsPermissions.Read,
    ];

    private static readonly string[] HostManagerPermissions =
    [
        PlatformPermissions.Read,
        PlatformPermissions.HostManageRead,
        PlatformPermissions.Create,
        RegionsPermissions.Read,
        RegionsPermissions.Manage,
        RegionsPermissions.Create,
    ];

    [TestMethod]
    public void Project_hides_host_management_menus_in_tenant_data_context()
    {
        var catalog = AuthorizationCatalog.Create(
            [new PlatformAuthorizationContributor(), new RegionsAuthorizationContributor()]);
        var projector = new NavigationProjector(catalog);

        var nodes = projector.Project(
            HostManagerPermissions,
            isHostDataContext: false);

        var routeNames = nodes.Select(node => node.RouteName).ToHashSet(StringComparer.Ordinal);
        Assert.IsTrue(routeNames.Contains("my-release-notes"));
        Assert.IsFalse(routeNames.Contains("host-release-notes"));
        Assert.IsFalse(routeNames.Contains("administrative-regions"));
    }

    [TestMethod]
    public void Project_keeps_host_management_menus_in_host_data_context()
    {
        var catalog = AuthorizationCatalog.Create(
            [new PlatformAuthorizationContributor(), new RegionsAuthorizationContributor()]);
        var projector = new NavigationProjector(catalog);

        var nodes = projector.Project(
            HostManagerPermissions,
            isHostDataContext: true);

        var routeNames = nodes.Select(node => node.RouteName).ToHashSet(StringComparer.Ordinal);
        Assert.IsTrue(routeNames.Contains("my-release-notes"));
        Assert.IsTrue(routeNames.Contains("host-release-notes"));
        Assert.IsTrue(routeNames.Contains("administrative-regions"));
    }

    [TestMethod]
    public void Project_hides_host_management_menus_when_tenant_only_has_read_permissions()
    {
        var catalog = AuthorizationCatalog.Create(
            [new PlatformAuthorizationContributor(), new RegionsAuthorizationContributor()]);
        var projector = new NavigationProjector(catalog);

        var nodes = projector.Project(
            TenantReaderPermissions,
            isHostDataContext: true);

        var routeNames = nodes.Select(node => node.RouteName).ToHashSet(StringComparer.Ordinal);
        Assert.IsTrue(routeNames.Contains("my-release-notes"));
        Assert.IsFalse(routeNames.Contains("host-release-notes"));
        Assert.IsFalse(routeNames.Contains("administrative-regions"));
    }
}
