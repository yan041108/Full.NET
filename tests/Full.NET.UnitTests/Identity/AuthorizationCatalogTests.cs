using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Tenancy;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class AuthorizationCatalogTests
{
    [TestMethod]
    public void Built_in_contributors_publish_the_initial_permission_set()
    {
        var catalog = AuthorizationCatalog.Create(
            [new IdentityAuthorizationContributor(), new TenancyAuthorizationContributor()]);

        CollectionAssert.AreEqual(
            new[]
            {
                "identity.api_keys.read",
                "identity.api_keys.write",
                "identity.menus.read",
                "identity.menus.write",
                "identity.navigation.read",
                "identity.roles.read",
                "identity.roles.write",
                "identity.sessions.read",
                "identity.sessions.write",
                "identity.super_administrators.manage",
                "identity.super_administrators.read",
                "identity.users.read",
                "identity.users.write",
                "platform.dashboard.read",
                "tenancy.host_tenants.read",
                "tenancy.tenant_packages.read",
                "tenancy.tenant_packages.write",
                "tenancy.tenants.read",
                "tenancy.tenants.switch",
                "tenancy.tenants.write",
            },
            catalog.Permissions.Select(permission => permission.Code).ToArray());
    }

    [TestMethod]
    public void Create_sorts_permissions_and_navigation_deterministically()
    {
        var contributor = new StubContributor(
            [
                new PermissionDefinition("z.read", "Z", AuthorizationScope.Host),
                new PermissionDefinition("a.read", "A", AuthorizationScope.Host),
            ],
            [
                new NavigationDefinition(
                    "z",
                    null,
                    "z-route",
                    "/z",
                    "overview",
                    "Z",
                    "Zulu",
                    "grid",
                    20,
                    "z.read"),
                new NavigationDefinition(
                    "a",
                    null,
                    "a-route",
                    "/a",
                    "overview",
                    "A",
                    "Alpha",
                    "grid",
                    10,
                    "a.read"),
            ]);

        var catalog = AuthorizationCatalog.Create([contributor]);

        CollectionAssert.AreEqual(
            new[] { "a.read", "z.read" },
            catalog.Permissions.Select(permission => permission.Code).ToArray());
        CollectionAssert.AreEqual(
            new[] { "a", "z" },
            catalog.Navigation.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    [DataRow(InvalidCatalogKind.DuplicatePermission)]
    [DataRow(InvalidCatalogKind.DuplicateNavigation)]
    [DataRow(InvalidCatalogKind.MissingParent)]
    [DataRow(InvalidCatalogKind.UnknownPermission)]
    [DataRow(InvalidCatalogKind.ParentCycle)]
    public void Create_rejects_invalid_catalogs(InvalidCatalogKind kind)
    {
        var contributor = CreateInvalidContributor(kind);

        Assert.Throws<InvalidOperationException>(
            () => AuthorizationCatalog.Create([contributor]));
    }

    private static StubContributor CreateInvalidContributor(InvalidCatalogKind kind)
    {
        var permissions = kind == InvalidCatalogKind.DuplicatePermission
            ? new[]
            {
                new PermissionDefinition("same.read", "A", AuthorizationScope.Host),
                new PermissionDefinition("same.read", "B", AuthorizationScope.Host),
            }
            : new[]
            {
                new PermissionDefinition("known.read", "Known", AuthorizationScope.Host),
            };
        NavigationDefinition[] navigation = kind switch
        {
            InvalidCatalogKind.DuplicateNavigation =>
            [
                CreateNavigation("same", null, "known.read"),
                CreateNavigation("same", null, "known.read"),
            ],
            InvalidCatalogKind.MissingParent =>
            [
                CreateNavigation("child", "missing", "known.read"),
            ],
            InvalidCatalogKind.UnknownPermission =>
            [
                CreateNavigation("unknown", null, "missing.read"),
            ],
            InvalidCatalogKind.ParentCycle =>
            [
                CreateNavigation("a", "b", "known.read"),
                CreateNavigation("b", "a", "known.read"),
            ],
            _ => [],
        };

        return new StubContributor(permissions, navigation);
    }

    private static NavigationDefinition CreateNavigation(
        string id,
        string? parentId,
        string permission)
    {
        return new NavigationDefinition(
            id,
            parentId,
            $"{id}-route",
            $"/{id}",
            "overview",
            id,
            id,
            "grid",
            10,
            permission);
    }

    private sealed class StubContributor(
        IReadOnlyCollection<PermissionDefinition> permissions,
        IReadOnlyCollection<NavigationDefinition> navigation)
        : IAuthorizationCatalogContributor
    {
        public IReadOnlyCollection<PermissionDefinition> Permissions { get; } = permissions;

        public IReadOnlyCollection<NavigationDefinition> Navigation { get; } = navigation;
    }

    public enum InvalidCatalogKind
    {
        DuplicatePermission,
        DuplicateNavigation,
        MissingParent,
        UnknownPermission,
        ParentCycle,
    }
}
