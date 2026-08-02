using System.Reflection;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.GetAuthorizationTree;
using Full.NET.Modules.Tenancy;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class AuthorizationTreeProjectorTests
{
    [TestMethod]
    public void ProjectHostTree_orders_pages_and_actions_deterministically()
    {
        var catalog = AuthorizationCatalog.Create([new StubContributor()]);
        var projector = new AuthorizationTreeProjector(catalog);

        var result = projector.ProjectHostTree();

        CollectionAssert.AreEqual(
            new[] { "standalone", "parent" },
            result.Select(page => page.Id).ToArray());
        CollectionAssert.AreEqual(
            new[] { "a", "b" },
            result.Single(page => page.Id == "parent").Children.Select(page => page.Id).ToArray());
        CollectionAssert.AreEqual(
            new[] { "standalone.create", "standalone.export" },
            result.Single(page => page.Id == "standalone").Actions.Select(action => action.Id).ToArray());
        Assert.AreEqual(
            "standalone.read",
            result.Single(page => page.Id == "standalone").PermissionCode);
    }

    [TestMethod]
    public void ProjectHostTree_excludes_super_administrator_pages_and_permissions()
    {
        var catalog = AuthorizationCatalog.Create(
            [new IdentityAuthorizationContributor(), new TenancyAuthorizationContributor()]);
        var projector = new AuthorizationTreeProjector(catalog);

        var pages = projector.ProjectHostTree();
        var permissionCodes = pages
            .SelectMany(FlattenPages)
            .SelectMany(page => page.Actions.Select(action => action.PermissionCode)
                .Prepend(page.PermissionCode))
            .ToArray();

        Assert.IsFalse(pages.SelectMany(FlattenPages).Any(page => page.Id == "super-administrators"));
        Assert.IsFalse(permissionCodes.Any(
            code => code.StartsWith("identity.super_administrators.", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ProjectHostTree_filters_tenant_only_permissions()
    {
        var catalog = AuthorizationCatalog.Create([new StubContributor()]);
        var projector = new AuthorizationTreeProjector(catalog);

        var result = projector.ProjectHostTree();

        Assert.IsFalse(result.Any(page => page.Id == "tenant-only"));
        Assert.IsFalse(result.SelectMany(FlattenPages).Any(
            page => page.Actions.Any(action => action.PermissionCode == "tenant.action")));
    }

    [TestMethod]
    public void ProjectHostTree_does_not_expose_client_component_metadata()
    {
        var properties = typeof(AuthorizationTreePageResponse)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();
        var actionProperties = typeof(AuthorizationTreeActionResponse)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[] { "Actions", "Children", "Id", "Order", "PermissionCode", "Title" },
            properties);
        CollectionAssert.AreEquivalent(
            new[] { "Id", "Name", "Order", "PermissionCode" },
            actionProperties);
    }

    [TestMethod]
    public void ProjectHostTree_includes_users_action_bindings_from_identity_catalog()
    {
        var catalog = AuthorizationCatalog.Create(
            [new IdentityAuthorizationContributor(), new TenancyAuthorizationContributor()]);
        var projector = new AuthorizationTreeProjector(catalog);

        var usersPage = projector.ProjectHostTree()
            .Single(page => page.Id == "users");

        CollectionAssert.AreEqual(
            new[]
            {
                "identity.users.create",
                "identity.users.update",
                "identity.users.assign_roles",
                "identity.users.reset_password",
                "identity.users.disable",
                "identity.users.enable",
                "identity.users.export",
            },
            usersPage.Actions.Select(action => action.PermissionCode).ToArray());
        Assert.AreEqual("identity.users.read", usersPage.PermissionCode);
    }

    private static IEnumerable<AuthorizationTreePageResponse> FlattenPages(
        AuthorizationTreePageResponse page)
    {
        yield return page;
        foreach (var child in page.Children.SelectMany(FlattenPages))
        {
            yield return child;
        }
    }

    private sealed class StubContributor : IAuthorizationCatalogContributor
    {
        public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
        [
            new PermissionDefinition("parent.read", "Parent", AuthorizationScope.Host),
            new PermissionDefinition("a.read", "A", AuthorizationScope.Host),
            new PermissionDefinition("b.read", "B", AuthorizationScope.Host),
            new PermissionDefinition("standalone.read", "Standalone", AuthorizationScope.Host),
            new PermissionDefinition("standalone.create", "Create", AuthorizationScope.Host),
            new PermissionDefinition("standalone.export", "Export", AuthorizationScope.Host),
            new PermissionDefinition("tenant.only", "Tenant Only", AuthorizationScope.Tenant),
            new PermissionDefinition("tenant.action", "Tenant Action", AuthorizationScope.Tenant),
        ];

        public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
        [
            CreateNavigation("parent", null, "parent.read", 10),
            CreateNavigation("b", "parent", "b.read", 30),
            CreateNavigation("a", "parent", "a.read", 20),
            CreateNavigation("standalone", null, "standalone.read", 5),
            CreateNavigation("tenant-only", null, "tenant.only", 99),
        ];

        public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
        [
            new AuthorizationActionDefinition(
                "standalone.export",
                "standalone",
                "standalone.export",
                "Export",
                "export",
                20),
            new AuthorizationActionDefinition(
                "standalone.create",
                "standalone",
                "standalone.create",
                "Create",
                "create",
                10),
            new AuthorizationActionDefinition(
                "tenant.only.action",
                "tenant-only",
                "tenant.action",
                "Tenant",
                "tenant",
                10),
        ];

        private static NavigationDefinition CreateNavigation(
            string id,
            string? parentId,
            string permission,
            int order) => new(
                id,
                parentId,
                $"{id}-route",
                $"/{id}",
                "overview",
                id,
                id,
                "grid",
                order,
                permission);
    }
}