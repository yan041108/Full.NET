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
                "identity.menus.create",
                "identity.menus.disable",
                "identity.menus.read",
                "identity.menus.update",
                "identity.modules.read",
                "identity.navigation.read",
                "identity.role_field_grants.read",
                "identity.role_field_grants.replace",
                "identity.roles.assign_data_scope",
                "identity.roles.assign_permissions",
                "identity.roles.create",
                "identity.roles.disable",
                "identity.roles.read",
                "identity.roles.update",
                "identity.sessions.read",
                "identity.sessions.write",
                "identity.super_administrators.manage",
                "identity.super_administrators.read",
                "identity.users.assign_roles",
                "identity.users.create",
                "identity.users.disable",
                "identity.users.enable",
                "identity.users.export",
                "identity.users.read",
                "identity.users.reset_password",
                "identity.users.update",
                "platform.dashboard.read",
                "tenancy.host_tenants.read",
                "tenancy.tenant_packages.read",
                "tenancy.tenant_packages.write",
                "tenancy.tenants.read",
                "tenancy.tenants.switch",
                "tenancy.tenants.write",
            },
            catalog.Permissions.Select(permission => permission.Code).ToArray());

        var navigation = catalog.Navigation.Single(item => item.Id == "modules");

        Assert.AreEqual("modules", navigation.RouteName);
        Assert.AreEqual("/identity/modules", navigation.Path);
        Assert.AreEqual("modules", navigation.ComponentKey);
        Assert.AreEqual(
            ModuleCatalogPermissions.Read,
            navigation.RequiredPermission);
    }

    [TestMethod]
    public void Host_users_actions_bind_to_exact_permissions()
    {
        var catalog = AuthorizationCatalog.Create(
            [new IdentityAuthorizationContributor(), new TenancyAuthorizationContributor()]);

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["create"] = "identity.users.create",
            ["update"] = "identity.users.update",
            ["assign-roles"] = "identity.users.assign_roles",
            ["reset-password"] = "identity.users.reset_password",
            ["disable"] = "identity.users.disable",
            ["enable"] = "identity.users.enable",
            ["export"] = "identity.users.export",
        };

        var usersActions = catalog.Actions
            .Where(action => action.NavigationId == "users")
            .ToDictionary(
                action => action.ClientActionKey,
                action => action.PermissionCode,
                StringComparer.Ordinal);

        CollectionAssert.AreEquivalent(
            expected,
            usersActions);
        Assert.IsFalse(catalog.Permissions.Any(
            permission => permission.Code == IdentityUserManagementPermissions.Write));
    }

    [TestMethod]
    public void Host_roles_actions_bind_to_exact_permissions()
    {
        var catalog = AuthorizationCatalog.Create(
            [new IdentityAuthorizationContributor(), new TenancyAuthorizationContributor()]);

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["create"] = "identity.roles.create",
            ["update"] = "identity.roles.update",
            ["assign-permissions"] = "identity.roles.assign_permissions",
            ["disable"] = "identity.roles.disable",
            ["assign-data-scope"] = "identity.roles.assign_data_scope",
            ["replace-field-grants"] = "identity.role_field_grants.replace",
        };

        var rolesActions = catalog.Actions
            .Where(action => action.NavigationId == "roles")
            .ToDictionary(
                action => action.ClientActionKey,
                action => action.PermissionCode,
                StringComparer.Ordinal);

        CollectionAssert.AreEquivalent(
            expected,
            rolesActions);
        Assert.IsFalse(catalog.Permissions.Any(
            permission => permission.Code == IdentityRoleManagementPermissions.Write));
    }

    [TestMethod]
    public void Host_role_field_grants_actions_bind_to_exact_permissions()
    {
        var catalog = AuthorizationCatalog.Create(
            [new IdentityAuthorizationContributor(), new TenancyAuthorizationContributor()]);

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["replace-field-grants"] = "identity.role_field_grants.replace",
        };

        var fieldGrantActions = catalog.Actions
            .Where(action => action.PermissionCode.StartsWith(
                "identity.role_field_grants.",
                StringComparison.Ordinal))
            .ToDictionary(
                action => action.ClientActionKey,
                action => action.PermissionCode,
                StringComparer.Ordinal);

        CollectionAssert.AreEquivalent(
            expected,
            fieldGrantActions);
        Assert.IsFalse(catalog.Permissions.Any(
            permission => permission.Code == IdentityRoleFieldGrantPermissions.Write));
    }

    [TestMethod]
    public void Host_menus_actions_bind_to_exact_permissions()
    {
        var catalog = AuthorizationCatalog.Create(
            [new IdentityAuthorizationContributor(), new TenancyAuthorizationContributor()]);

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["create"] = "identity.menus.create",
            ["update"] = "identity.menus.update",
            ["disable"] = "identity.menus.disable",
        };

        var menusActions = catalog.Actions
            .Where(action => action.NavigationId == "menus")
            .ToDictionary(
                action => action.ClientActionKey,
                action => action.PermissionCode,
                StringComparer.Ordinal);

        CollectionAssert.AreEquivalent(
            expected,
            menusActions);
        Assert.IsFalse(catalog.Permissions.Any(
            permission => permission.Code == IdentityMenuManagementPermissions.Write));
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
    public void Create_rejects_action_with_unknown_navigation()
    {
        var contributor = new StubContributor(
            [new PermissionDefinition("identity.users.read", "查看用户", AuthorizationScope.Host)],
            [],
            [new AuthorizationActionDefinition(
                "identity.users.create",
                "missing-users-page",
                "identity.users.create",
                "创建用户",
                "create",
                10)]);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => AuthorizationCatalog.Create([contributor]));
    }

    [TestMethod]
    public void Create_rejects_action_with_unknown_permission()
    {
        var contributor = new StubContributor(
            [new PermissionDefinition("identity.users.read", "查看用户", AuthorizationScope.Host)],
            [CreateNavigation("users", null, "identity.users.read")],
            [new AuthorizationActionDefinition(
                "identity.users.create",
                "users",
                "identity.users.create",
                "创建用户",
                "create",
                10)]);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => AuthorizationCatalog.Create([contributor]));
    }

    [TestMethod]
    public void Create_rejects_duplicate_action_id()
    {
        var contributor = new StubContributor(
            [
                new PermissionDefinition("identity.users.read", "查看用户", AuthorizationScope.Host),
                new PermissionDefinition("identity.users.create", "创建用户", AuthorizationScope.Host),
                new PermissionDefinition("identity.users.update", "更新用户", AuthorizationScope.Host),
            ],
            [CreateNavigation("users", null, "identity.users.read")],
            [
                new AuthorizationActionDefinition(
                    "identity.users.create",
                    "users",
                    "identity.users.create",
                    "创建用户",
                    "create",
                    10),
                new AuthorizationActionDefinition(
                    "identity.users.create",
                    "users",
                    "identity.users.update",
                    "更新用户",
                    "update",
                    20),
            ]);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => AuthorizationCatalog.Create([contributor]));
    }

    [TestMethod]
    public void Create_rejects_duplicate_navigation_and_client_action_key()
    {
        var contributor = new StubContributor(
            [
                new PermissionDefinition("identity.users.read", "查看用户", AuthorizationScope.Host),
                new PermissionDefinition("identity.users.create", "创建用户", AuthorizationScope.Host),
                new PermissionDefinition("identity.users.update", "更新用户", AuthorizationScope.Host),
            ],
            [CreateNavigation("users", null, "identity.users.read")],
            [
                new AuthorizationActionDefinition(
                    "identity.users.create",
                    "users",
                    "identity.users.create",
                    "创建用户",
                    "create",
                    10),
                new AuthorizationActionDefinition(
                    "identity.users.create-alt",
                    "users",
                    "identity.users.update",
                    "更新用户",
                    "create",
                    20),
            ]);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => AuthorizationCatalog.Create([contributor]));
    }

    [TestMethod]
    public void Create_sorts_actions_by_navigation_order_action_order_and_id()
    {
        var contributor = new StubContributor(
            [
                new PermissionDefinition("a.read", "A Read", AuthorizationScope.Host),
                new PermissionDefinition("a.create", "A Create", AuthorizationScope.Host),
                new PermissionDefinition("b.read", "B Read", AuthorizationScope.Host),
                new PermissionDefinition("b.create", "B Create", AuthorizationScope.Host),
            ],
            [
                CreateNavigation("a-page", null, "a.read", order: 10),
                CreateNavigation("b-page", null, "b.read", order: 20),
            ],
            [
                new AuthorizationActionDefinition(
                    "b.create",
                    "b-page",
                    "b.create",
                    "B Create",
                    "create",
                    10),
                new AuthorizationActionDefinition(
                    "a.create",
                    "a-page",
                    "a.create",
                    "A Create",
                    "create",
                    10),
            ]);

        var catalog = AuthorizationCatalog.Create([contributor]);

        CollectionAssert.AreEqual(
            new[] { "a.create", "b.create" },
            catalog.Actions.Select(action => action.Id).ToArray());
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
        string permission,
        int order = 10)
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
            order,
            permission);
    }

    private sealed class StubContributor(
        IReadOnlyCollection<PermissionDefinition> permissions,
        IReadOnlyCollection<NavigationDefinition> navigation,
        IReadOnlyCollection<AuthorizationActionDefinition>? actions = null)
        : IAuthorizationCatalogContributor
    {
        public IReadOnlyCollection<PermissionDefinition> Permissions { get; } = permissions;

        public IReadOnlyCollection<NavigationDefinition> Navigation { get; } = navigation;

        public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
            actions ?? [];
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
