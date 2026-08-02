using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.GetNavigation;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class NavigationProjectorTests
{
    [TestMethod]
    public void Project_filters_unauthorized_nodes_and_preserves_stable_tree_order()
    {
        var catalog = AuthorizationCatalog.Create(
            [new StubContributor()]);
        var projector = new NavigationProjector(catalog);

        var result = projector.Project(
            ["parent.read", "b.read", "a.read"]);

        Assert.HasCount(1, result);
        Assert.AreEqual("parent", result[0].Id);
        CollectionAssert.AreEqual(
            new[] { "a", "b" },
            result[0].Children.Select(item => item.Id).ToArray());
        Assert.IsFalse(result[0].Children.Any(item => item.Id == "hidden"));
    }

    [TestMethod]
    public void Project_merges_additional_definitions_with_catalog()
    {
        var catalog = AuthorizationCatalog.Create(
            [new StubContributor()]);
        var projector = new NavigationProjector(catalog);
        var additional = new NavigationDefinition(
            "custom-menu",
            null,
            "custom-menu",
            "/",
            "overview",
            "Custom",
            "Custom",
            "grid",
            5,
            "parent.read");

        var result = projector.Project(
            ["parent.read", "a.read", "b.read"],
            [additional]);

        Assert.IsTrue(result.Any(node => node.RouteName == "custom-menu"));
    }

    [TestMethod]
    public void Project_removes_parent_when_no_child_is_authorized()
    {
        var catalog = AuthorizationCatalog.Create(
            [new StubContributor()]);
        var projector = new NavigationProjector(catalog);

        var result = projector.Project(["parent.read"]);

        Assert.HasCount(0, result);
    }

    private sealed class StubContributor : IAuthorizationCatalogContributor
    {
        public AuthorizationModuleDefinition Module { get; } =
            new("test", "测试模块", 1);

        public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
        [
            new PermissionDefinition("parent.read", "Parent", AuthorizationScope.Host),
            new PermissionDefinition("a.read", "A", AuthorizationScope.Host),
            new PermissionDefinition("b.read", "B", AuthorizationScope.Host),
            new PermissionDefinition("hidden.read", "Hidden", AuthorizationScope.Host),
        ];

        public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
        [
            Create("parent", null, "parent.read", 1),
            Create("b", "parent", "b.read", 20),
            Create("a", "parent", "a.read", 10),
            Create("hidden", "parent", "hidden.read", 30),
        ];

        private static NavigationDefinition Create(
            string id,
            string? parentId,
            string permission,
            int order) => new(
                id,
                parentId,
                $"{id}-route",
                $"/{id}",
                id == "parent" ? "overview" : "tenant-context",
                id,
                id,
                "grid",
                order,
                permission);
    }
}
