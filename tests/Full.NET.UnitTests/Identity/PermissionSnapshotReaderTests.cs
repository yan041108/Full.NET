using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Tenancy;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class PermissionSnapshotReaderTests
{
    [TestMethod]
    public async Task Read_intersects_database_grants_with_the_code_catalog()
    {
        var query = new StubQueryExecutor([
                new IdentityAuthorizationRow("tenancy.tenants.switch", false),
                new IdentityAuthorizationRow("removed.permission", false),
                new IdentityAuthorizationRow("platform.dashboard.read", false),
                new IdentityAuthorizationRow("tenancy.tenants.switch", false),
            ]);
        var catalog = AuthorizationCatalog.Create(
            [new IdentityAuthorizationContributor(), new TenancyAuthorizationContributor()]);
        var reader = new PermissionSnapshotReader(query, catalog);

        var snapshot = await reader.ReadAsync(
            Guid.Parse("01981a3f-00c0-7000-8000-000000000010"),
            "host",
            null,
            default);

        CollectionAssert.AreEqual(
            new[] { "platform.dashboard.read", "tenancy.tenants.switch" },
            snapshot.Permissions.ToArray());
        Assert.IsFalse(snapshot.IsSuperAdministrator);
    }

    [TestMethod]
    public async Task Super_administrator_receives_all_catalog_permissions_for_effective_scope()
    {
        var query = new StubQueryExecutor(
            [new IdentityAuthorizationRow(null, true)]);
        var catalog = AuthorizationCatalog.Create(
            [new IdentityAuthorizationContributor(), new TenancyAuthorizationContributor()]);
        var reader = new PermissionSnapshotReader(query, catalog);

        var snapshot = await reader.ReadAsync(
            Guid.Parse("01981a3f-00c0-7000-8000-000000000010"),
            "host",
            Guid.Parse("01981a3f-00c0-7000-8000-000000000011"),
            default);

        Assert.IsTrue(snapshot.IsSuperAdministrator);
        CollectionAssert.AreEqual(
            new[]
            {
                "identity.navigation.read",
                "platform.dashboard.read",
                "tenancy.tenants.read",
                "tenancy.tenants.switch",
            },
            snapshot.Permissions.ToArray());
    }

    private sealed class StubQueryExecutor(
        IReadOnlyList<IdentityAuthorizationRow> authorization)
        : IQueryExecutor
    {
        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Assert.AreEqual(IdentitySql.GetUserAuthorization, statement);
            return Task.FromResult<IReadOnlyList<T>>(
                authorization.Cast<T>().ToArray());
        }
    }
}
